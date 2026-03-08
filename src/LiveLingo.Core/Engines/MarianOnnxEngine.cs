using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LiveLingo.Core.Engines;

public sealed class MarianOnnxEngine : ITranslationEngine
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<MarianOnnxEngine> _logger;
    private readonly ConcurrentDictionary<string, ModelSession> _sessions = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; }

    public MarianOnnxEngine(IModelManager modelManager, ILogger<MarianOnnxEngine> logger)
    {
        _modelManager = modelManager;
        _logger = logger;
        SupportedLanguages = ModelRegistry.TranslationModels
            .SelectMany(m =>
            {
                var parts = m.Id.Replace("opus-mt-", "").Split('-');
                return parts.Length == 2
                    ? [parts[0], parts[1]]
                    : Array.Empty<string>();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new LanguageInfo(code, code))
            .ToList();
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var descriptor = ModelRegistry.FindTranslationModel(sourceLanguage, targetLanguage);
        if (descriptor is null)
            throw new NotSupportedException(
                $"No translation model available for {sourceLanguage}→{targetLanguage}");

        var session = await GetOrCreateSessionAsync(descriptor, ct);
        return await Task.Run(() => session.Translate(text, sourceLanguage, targetLanguage, ct), ct)
            .ConfigureAwait(false);
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        ModelRegistry.FindTranslationModel(sourceLanguage, targetLanguage) is not null;

    private async Task<ModelSession> GetOrCreateSessionAsync(ModelDescriptor descriptor, CancellationToken ct)
    {
        if (_sessions.TryGetValue(descriptor.Id, out var existing))
            return existing;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_sessions.TryGetValue(descriptor.Id, out existing))
                return existing;

            await _modelManager.EnsureModelAsync(descriptor, null, ct);
            var modelDir = _modelManager.GetModelDirectory(descriptor.Id);

            var session = new ModelSession(modelDir, _logger);
            _sessions.TryAdd(descriptor.Id, session);
            return session;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _loadLock.Dispose();
    }

    internal sealed class ModelSession : IDisposable
    {
        private readonly InferenceSession _encoderSession;
        private readonly InferenceSession _decoderSession;
        private readonly ILogger _logger;
        private readonly SentencePieceTokenizer _sourceTokenizer;
        private readonly SentencePieceTokenizer _targetTokenizer;
        private readonly IReadOnlyDictionary<int, string> _sourceIdToToken;
        private readonly IReadOnlyDictionary<string, int> _targetTokenToId;
        private readonly IReadOnlyDictionary<string, int> _modelTokenToId;
        private readonly IReadOnlyDictionary<int, string> _modelIdToToken;
        private readonly string _encoderInputIdsName;
        private readonly string? _encoderAttentionMaskName;
        private readonly string _encoderOutputName;
        private readonly string _decoderInputIdsName;
        private readonly string? _decoderAttentionMaskName;
        private readonly string _decoderEncoderHiddenStatesName;
        private readonly string? _decoderEncoderAttentionMaskName;
        private readonly string _decoderLogitsOutputName;
        private readonly string? _decoderUseCacheBranchName;
        private readonly IReadOnlyList<(string InputName, string OutputName)> _decoderPastBindings;
        private readonly bool _supportsDecoderCache;
        private readonly int _targetBosId;
        private readonly int _targetEosId;
        private readonly int _modelPadId;
        private readonly int _modelUnkId;
        private readonly int _targetVocabSize;
        private const int MaxDecodeTokens = 512;
        private const int MinDecodeTokens = 128;
        private const int MaxChunkCharacters = 96;

        public ModelSession(string modelDir, ILogger logger)
        {
            _logger = logger;
            if (!Directory.Exists(modelDir))
                throw new DirectoryNotFoundException($"Model directory not found: {modelDir}");

            var missingFiles = GetMissingRequiredFiles(modelDir).ToArray();
            if (missingFiles.Length > 0)
            {
                throw new FileNotFoundException(
                    $"Marian model files are incomplete in '{modelDir}'. Missing: {string.Join(", ", missingFiles)}");
            }

            var encoderPath = Path.Combine(modelDir, "onnx", "encoder_model.onnx");
            var decoderPath = Path.Combine(modelDir, "onnx", "decoder_model_merged.onnx");
            var options = new SessionOptions { InterOpNumThreads = 2, IntraOpNumThreads = 2 };
            _encoderSession = new InferenceSession(encoderPath, options);
            _decoderSession = new InferenceSession(decoderPath, options);
            using (var sourceStream = File.OpenRead(Path.Combine(modelDir, "source.spm")))
                _sourceTokenizer = SentencePieceTokenizer.Create(sourceStream, false, false, null);
            using (var targetStream = File.OpenRead(Path.Combine(modelDir, "target.spm")))
                _targetTokenizer = SentencePieceTokenizer.Create(targetStream, false, false, null);
            _sourceIdToToken = _sourceTokenizer.Vocabulary.ToDictionary(k => k.Value, k => k.Key);
            _targetTokenToId = _targetTokenizer.Vocabulary;
            var (modelTokenToId, modelIdToToken) = LoadModelVocab(modelDir);
            _modelTokenToId = modelTokenToId;
            _modelIdToToken = modelIdToToken;

            _targetBosId = _targetTokenizer.BeginningOfSentenceId;
            _targetEosId = 0;
            _modelPadId = _modelTokenToId.TryGetValue("<pad>", out var padIdFromVocab) ? padIdFromVocab : 65000;
            _modelUnkId = _modelTokenToId.TryGetValue("<unk>", out var unkIdFromVocab) ? unkIdFromVocab : 1;
            var generationIds = LoadGenerationIds(modelDir);
            if (generationIds.decoderStartTokenId is int decoderStartTokenId)
                _targetBosId = decoderStartTokenId;
            if (generationIds.eosTokenId is int eosTokenId)
                _targetEosId = eosTokenId;
            if (generationIds.padTokenId is int padTokenId)
                _modelPadId = padTokenId;
            if (_targetBosId < 0) _targetBosId = 0;
            if (_targetEosId < 0) _targetEosId = 0;
            _targetVocabSize = _targetTokenizer.Vocabulary.Count;

            _encoderInputIdsName = ResolveRequiredName(_encoderSession.InputMetadata.Keys, ["input_ids"]);
            _encoderAttentionMaskName = ResolveOptionalName(_encoderSession.InputMetadata.Keys, ["attention_mask", "encoder_attention_mask"]);
            _encoderOutputName = ResolveOutputName(_encoderSession.OutputMetadata.Keys, "last_hidden_state");

            _decoderInputIdsName = ResolveRequiredName(_decoderSession.InputMetadata.Keys, ["input_ids", "decoder_input_ids"]);
            _decoderAttentionMaskName = ResolveOptionalName(_decoderSession.InputMetadata.Keys, ["decoder_attention_mask"]);
            _decoderEncoderHiddenStatesName = ResolveRequiredName(
                _decoderSession.InputMetadata.Keys,
                ["encoder_hidden_states", "encoder_outputs", "encoder_last_hidden_state"]);
            _decoderEncoderAttentionMaskName = ResolveOptionalName(_decoderSession.InputMetadata.Keys, ["encoder_attention_mask", "attention_mask"]);
            _decoderUseCacheBranchName = ResolveOptionalName(_decoderSession.InputMetadata.Keys, ["use_cache_branch"]);
            _decoderLogitsOutputName = ResolveOutputName(_decoderSession.OutputMetadata.Keys, "logits");
            _decoderPastBindings = BuildDecoderPastBindings(
                _decoderSession.InputMetadata.Keys,
                _decoderSession.OutputMetadata.Keys);
            _supportsDecoderCache = false;

            _logger.LogDebug(
                "Loaded Marian ONNX sessions from {EncoderPath} and {DecoderPath}; decoderStartTokenId={DecoderStart}, eosTokenId={Eos}, supportsCache={SupportsCache}, cacheBindings={BindingCount}",
                encoderPath, decoderPath, _targetBosId, _targetEosId, _supportsDecoderCache, _decoderPastBindings.Count);
        }

        public string Translate(
            string text,
            string sourceLanguage,
            string targetLanguage,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return TranslatePreservingFormatting(text, ct);
        }

        private string TranslatePreservingFormatting(string text, CancellationToken ct)
        {
            var parts = Regex.Split(text, "(\r\n|\n|\r)");
            if (parts.Length == 1)
                return TranslateLongContent(text, ct);

            var output = new StringBuilder(text.Length + 32);
            for (var i = 0; i < parts.Length; i += 2)
            {
                var line = parts[i];
                output.Append(TranslateLineWithIndentation(line, ct));
                if (i + 1 < parts.Length)
                    output.Append(parts[i + 1]);
            }

            return output.ToString();
        }

        private string TranslateLineWithIndentation(string line, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(line))
                return line;

            var leadingCount = 0;
            while (leadingCount < line.Length && (line[leadingCount] == ' ' || line[leadingCount] == '\t'))
                leadingCount++;

            var trailingCount = 0;
            var trailingIndex = line.Length - 1;
            while (trailingIndex >= leadingCount && (line[trailingIndex] == ' ' || line[trailingIndex] == '\t'))
            {
                trailingCount++;
                trailingIndex--;
            }

            var contentLength = line.Length - leadingCount - trailingCount;
            if (contentLength <= 0)
                return line;

            var content = line.Substring(leadingCount, contentLength);
            var translated = TranslateLongContent(content, ct);
            if (string.IsNullOrWhiteSpace(translated))
                translated = content;

            var leading = leadingCount > 0 ? line[..leadingCount] : string.Empty;
            var trailing = trailingCount > 0 ? line[(line.Length - trailingCount)..] : string.Empty;
            return string.Concat(leading, translated, trailing);
        }

        private string TranslateLongContent(string content, CancellationToken ct)
        {
            var chunks = SplitIntoChunks(content, MaxChunkCharacters);
            if (chunks.Count == 1)
                return TranslateChunkCore(chunks[0], ct);

            var translatedChunks = new List<string>(chunks.Count);
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var translated = TranslateChunkCore(chunk, ct);
                translatedChunks.Add(string.IsNullOrWhiteSpace(translated) ? chunk : translated);
            }

            var joined = string.Join(" ", translatedChunks.Where(s => !string.IsNullOrWhiteSpace(s)));
            joined = Regex.Replace(joined, @"\s+([,.;:!?])", "$1");
            return joined.Trim();
        }

        private static List<string> SplitIntoChunks(string text, int maxChars)
        {
            if (text.Length <= maxChars)
                return [text];

            var sentencePieces = Regex.Split(text, @"(?<=[。！？；!?;])")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            if (sentencePieces.Length == 0)
                return [text];

            var chunks = new List<string>();
            foreach (var piece in sentencePieces)
            {
                if (piece.Length > maxChars)
                {
                    for (var start = 0; start < piece.Length; start += maxChars)
                    {
                        var length = Math.Min(maxChars, piece.Length - start);
                        chunks.Add(piece.Substring(start, length));
                    }

                    continue;
                }

                chunks.Add(piece);
            }

            return chunks.Count > 0 ? chunks : [text];
        }

        private string TranslateChunkCore(string text, CancellationToken ct)
        {
            var sourceIds = EncodeSourceToModelIds(text);
            if (sourceIds.Length == 0)
                return text;

            var sourceAttentionMask = Enumerable.Repeat(1L, sourceIds.Length).ToArray();
            var encoderHiddenStates = RunEncoder(sourceIds, sourceAttentionMask, ct);
            var decodeBudget = Math.Clamp(sourceIds.Length * 3, MinDecodeTokens, MaxDecodeTokens);

            var generated = new List<long>();
            if (_supportsDecoderCache)
            {
                try
                {
                    var currentToken = (long)_targetBosId;
                    Dictionary<string, DenseTensor<float>>? pastKeyValues = null;
                    for (var step = 0; step < decodeBudget; step++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (nextToken, nextPast) = RunDecoderStepWithCache(
                            currentToken,
                            sourceAttentionMask,
                            encoderHiddenStates,
                            pastKeyValues,
                            ct);
                        if (nextToken == _targetEosId)
                            break;
                        generated.Add(nextToken);
                        currentToken = nextToken;
                        pastKeyValues = nextPast;
                    }
                }
                catch (OnnxRuntimeException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Decoder cache path failed, falling back to non-cache decoding.");
                    generated = DecodeWithoutCache(sourceAttentionMask, encoderHiddenStates, decodeBudget, ct);
                }
            }
            else
            {
                generated = DecodeWithoutCache(sourceAttentionMask, encoderHiddenStates, decodeBudget, ct);
            }

            var decodedTokenIds = generated
                .Where(id => id != _targetEosId)
                .ToArray();
            if (decodedTokenIds.Length == 0)
                return text;

            var result = DecodeModelTokenIds(decodedTokenIds);
            return string.IsNullOrWhiteSpace(result) ? text : result;
        }

        public void Dispose()
        {
            _decoderSession.Dispose();
            _encoderSession.Dispose();
        }

        private static IEnumerable<string> GetMissingRequiredFiles(string modelDir)
        {
            var requiredFiles = new[]
            {
                Path.Combine("onnx", "encoder_model.onnx"),
                Path.Combine("onnx", "decoder_model_merged.onnx"),
                "source.spm",
                "target.spm",
                "vocab.json",
            };
            foreach (var file in requiredFiles)
            {
                if (!File.Exists(Path.Combine(modelDir, file)))
                    yield return file;
            }
        }

        private static (int? decoderStartTokenId, int? eosTokenId, int? padTokenId) LoadGenerationIds(string modelDir)
        {
            int? decoderStartTokenId = null;
            int? eosTokenId = null;
            int? padTokenId = null;
            foreach (var fileName in new[] { "generation_config.json", "config.json" })
            {
                var path = Path.Combine(modelDir, fileName);
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var stream = File.OpenRead(path);
                    using var json = JsonDocument.Parse(stream);
                    var root = json.RootElement;
                    if (decoderStartTokenId is null &&
                        root.TryGetProperty("decoder_start_token_id", out var decoderStart) &&
                        decoderStart.ValueKind == JsonValueKind.Number &&
                        decoderStart.TryGetInt32(out var parsedDecoderStart))
                    {
                        decoderStartTokenId = parsedDecoderStart;
                    }

                    if (eosTokenId is null &&
                        root.TryGetProperty("eos_token_id", out var eos) &&
                        eos.ValueKind == JsonValueKind.Number &&
                        eos.TryGetInt32(out var parsedEos))
                    {
                        eosTokenId = parsedEos;
                    }

                    if (padTokenId is null &&
                        root.TryGetProperty("pad_token_id", out var pad) &&
                        pad.ValueKind == JsonValueKind.Number &&
                        pad.TryGetInt32(out var parsedPad))
                    {
                        padTokenId = parsedPad;
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed config and continue with tokenizer-based defaults.
                }
            }

            return (decoderStartTokenId, eosTokenId, padTokenId);
        }

        private static (Dictionary<string, int> tokenToId, Dictionary<int, string> idToToken) LoadModelVocab(string modelDir)
        {
            var path = Path.Combine(modelDir, "vocab.json");
            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            var tokenToId = new Dictionary<string, int>(StringComparer.Ordinal);
            var idToToken = new Dictionary<int, string>();
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt32(out var id))
                {
                    continue;
                }

                tokenToId[property.Name] = id;
                idToToken[id] = property.Name;
            }

            return (tokenToId, idToToken);
        }

        private long[] EncodeSourceToModelIds(string text)
        {
            var sourceSpmIds = _sourceTokenizer.EncodeToIds(
                text,
                addBeginningOfSentence: false,
                addEndOfSentence: false,
                considerPreTokenization: true,
                considerNormalization: true);
            var sourceModelIds = new List<long>(sourceSpmIds.Count + 1);
            foreach (var spmId in sourceSpmIds)
            {
                var token = _sourceIdToToken.TryGetValue(spmId, out var tokenText)
                    ? tokenText
                    : "<unk>";
                sourceModelIds.Add(_modelTokenToId.TryGetValue(token, out var modelId)
                    ? modelId
                    : _modelUnkId);
            }
            sourceModelIds.Add(_targetEosId);
            return sourceModelIds.ToArray();
        }

        private string DecodeModelTokenIds(IEnumerable<long> modelTokenIds)
        {
            var targetSpmIds = new List<int>();
            foreach (var modelTokenId in modelTokenIds)
            {
                if (modelTokenId == _targetEosId || modelTokenId == _modelPadId)
                    continue;
                if (!_modelIdToToken.TryGetValue((int)modelTokenId, out var token))
                    continue;
                if (token is "<pad>" or "</s>" or "<unk>")
                    continue;
                if (_targetTokenToId.TryGetValue(token, out var spmId))
                    targetSpmIds.Add(spmId);
            }

            if (targetSpmIds.Count == 0)
                return string.Empty;

            return _targetTokenizer.Decode(targetSpmIds, considerSpecialTokens: false).Trim();
        }

        private float[] RunEncoder(long[] sourceIds, long[] sourceAttentionMask, CancellationToken ct)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_encoderInputIdsName, ToTensor(sourceIds, sourceIds.Length))
            };
            if (_encoderAttentionMaskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_encoderAttentionMaskName, ToTensor(sourceAttentionMask, sourceAttentionMask.Length)));

            using var results = _encoderSession.Run(inputs);
            var output = results.FirstOrDefault(r => string.Equals(r.Name, _encoderOutputName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Encoder output '{_encoderOutputName}' was not produced.");
            return output.AsTensor<float>().ToArray();
        }

        private (long nextToken, Dictionary<string, DenseTensor<float>>? pastKeyValues) RunDecoderStepWithCache(
            long currentToken,
            long[] sourceAttentionMask,
            float[] encoderHiddenStates,
            Dictionary<string, DenseTensor<float>>? pastKeyValues,
            CancellationToken ct)
        {
            pastKeyValues ??= CreateInitialPastKeyValues(sourceAttentionMask.Length);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_decoderInputIdsName, ToTensor([currentToken], 1)),
                NamedOnnxValue.CreateFromTensor(
                    _decoderEncoderHiddenStatesName,
                    ToTensor(encoderHiddenStates, sourceAttentionMask.Length, encoderHiddenStates.Length / sourceAttentionMask.Length))
            };

            if (_decoderAttentionMaskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(_decoderAttentionMaskName, ToTensor([1L], 1)));

            if (_decoderEncoderAttentionMaskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    _decoderEncoderAttentionMaskName,
                    ToTensor(sourceAttentionMask, sourceAttentionMask.Length)));

            if (_decoderUseCacheBranchName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    _decoderUseCacheBranchName,
                    new DenseTensor<bool>(new[] { true }, [1])));

            foreach (var binding in _decoderPastBindings)
            {
                if (!pastKeyValues.TryGetValue(binding.InputName, out var pastTensor))
                    throw new InvalidOperationException($"Missing decoder cache input '{binding.InputName}'.");
                inputs.Add(NamedOnnxValue.CreateFromTensor(binding.InputName, pastTensor));
            }

            using var results = _decoderSession.Run(inputs);
            var logitsValue = results.FirstOrDefault(r => string.Equals(r.Name, _decoderLogitsOutputName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Decoder output '{_decoderLogitsOutputName}' was not produced.");
            var nextToken = ExtractNextTokenId(logitsValue.AsTensor<float>());

            Dictionary<string, DenseTensor<float>>? nextPast = null;
            if (_supportsDecoderCache)
            {
                nextPast = new Dictionary<string, DenseTensor<float>>(StringComparer.OrdinalIgnoreCase);
                foreach (var binding in _decoderPastBindings)
                {
                    var present = results.FirstOrDefault(r =>
                        string.Equals(r.Name, binding.OutputName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Decoder output '{binding.OutputName}' was not produced.");
                    var presentTensor = present.AsTensor<float>();
                    nextPast[binding.InputName] = CloneTensor(presentTensor);
                }
            }

            ct.ThrowIfCancellationRequested();
            return (nextToken, nextPast);
        }

        private List<long> DecodeWithoutCache(
            long[] sourceAttentionMask,
            float[] encoderHiddenStates,
            int maxDecodeTokens,
            CancellationToken ct)
        {
            var generatedWithBos = new List<long> { _targetBosId };
            for (var step = 0; step < maxDecodeTokens; step++)
            {
                ct.ThrowIfCancellationRequested();
                var nextToken = RunDecoderStepNoCache(generatedWithBos, sourceAttentionMask, encoderHiddenStates, ct);
                if (nextToken == _targetEosId)
                    break;
                generatedWithBos.Add(nextToken);
            }

            return generatedWithBos
                .Skip(1)
                .ToList();
        }

        private long RunDecoderStepNoCache(
            List<long> generatedIds,
            long[] sourceAttentionMask,
            float[] encoderHiddenStates,
            CancellationToken ct)
        {
            var generated = generatedIds.ToArray();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_decoderInputIdsName, ToTensor(generated, generated.Length)),
                NamedOnnxValue.CreateFromTensor(
                    _decoderEncoderHiddenStatesName,
                    ToTensor(encoderHiddenStates, sourceAttentionMask.Length, encoderHiddenStates.Length / sourceAttentionMask.Length))
            };

            if (_decoderAttentionMaskName is not null)
            {
                var decoderAttention = Enumerable.Repeat(1L, generated.Length).ToArray();
                inputs.Add(NamedOnnxValue.CreateFromTensor(_decoderAttentionMaskName, ToTensor(decoderAttention, decoderAttention.Length)));
            }

            if (_decoderEncoderAttentionMaskName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    _decoderEncoderAttentionMaskName,
                    ToTensor(sourceAttentionMask, sourceAttentionMask.Length)));

            if (_decoderUseCacheBranchName is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    _decoderUseCacheBranchName,
                    new DenseTensor<bool>(new[] { false }, [1])));

            using var results = _decoderSession.Run(inputs);
            var logitsValue = results.FirstOrDefault(r => string.Equals(r.Name, _decoderLogitsOutputName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Decoder output '{_decoderLogitsOutputName}' was not produced.");
            var nextToken = ExtractNextTokenId(logitsValue.AsTensor<float>());

            ct.ThrowIfCancellationRequested();
            return nextToken;
        }

        private long ExtractNextTokenId(Tensor<float> logits)
        {
            var dims = logits.Dimensions.ToArray();
            if (dims.Length < 3)
                throw new InvalidOperationException("Decoder logits must have shape [batch, sequence, vocab].");

            var values = logits.ToArray();
            var maxIndex = 0;
            var maxValue = float.MinValue;
            if (dims[2] == _targetVocabSize)
            {
                var sequenceLength = dims[1];
                var vocabSize = dims[2];
                var offset = (sequenceLength - 1) * vocabSize;
                for (var i = 0; i < vocabSize; i++)
                {
                    var value = values[offset + i];
                    if (value > maxValue)
                    {
                        maxValue = value;
                        maxIndex = i;
                    }
                }
            }
            else if (dims[1] == _targetVocabSize)
            {
                var vocabSize = dims[1];
                var sequenceLength = dims[2];
                var lastStep = sequenceLength - 1;
                for (var i = 0; i < vocabSize; i++)
                {
                    var value = values[(i * sequenceLength) + lastStep];
                    if (value > maxValue)
                    {
                        maxValue = value;
                        maxIndex = i;
                    }
                }
            }
            else
            {
                var sequenceLength = dims[1];
                var vocabSize = dims[2];
                var offset = (sequenceLength - 1) * vocabSize;
                for (var i = 0; i < vocabSize; i++)
                {
                    var value = values[offset + i];
                    if (value > maxValue)
                    {
                        maxValue = value;
                        maxIndex = i;
                    }
                }
            }

            return maxIndex;
        }

        private static DenseTensor<float> CloneTensor(Tensor<float> tensor) =>
            new(tensor.ToArray(), tensor.Dimensions.ToArray());

        private Dictionary<string, DenseTensor<float>> CreateInitialPastKeyValues(int encoderSequenceLength)
        {
            var initial = new Dictionary<string, DenseTensor<float>>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in _decoderPastBindings)
            {
                var metadata = _decoderSession.InputMetadata[binding.InputName];
                var dimensions = metadata.Dimensions.ToArray();
                var numHeads = dimensions.Length > 1 && dimensions[1] > 0 ? dimensions[1] : 8;
                var headDim = dimensions.Length > 3 && dimensions[3] > 0 ? dimensions[3] : 64;
                var sequenceLength = binding.InputName.Contains(".encoder.", StringComparison.OrdinalIgnoreCase)
                    ? encoderSequenceLength
                    : 0;
                var values = new float[numHeads * sequenceLength * headDim];
                initial[binding.InputName] = new DenseTensor<float>(
                    values,
                    [1, numHeads, sequenceLength, headDim]);
            }

            return initial;
        }

        private static DenseTensor<long> ToTensor(long[] values, int sequenceLength) =>
            new(values, [1, sequenceLength]);

        private static DenseTensor<float> ToTensor(float[] values, int sequenceLength, int hiddenSize) =>
            new(values, [1, sequenceLength, hiddenSize]);

        private static IReadOnlyList<(string InputName, string OutputName)> BuildDecoderPastBindings(
            IEnumerable<string> decoderInputNames,
            IEnumerable<string> decoderOutputNames)
        {
            var outputNames = decoderOutputNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bindings = new List<(string InputName, string OutputName)>();
            foreach (var inputName in decoderInputNames
                         .Where(n => n.StartsWith("past_key_values.", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var suffix = inputName["past_key_values.".Length..];
                var outputName = $"present.{suffix}";
                if (outputNames.Contains(outputName))
                    bindings.Add((inputName, outputName));
            }

            return bindings;
        }

        private static string ResolveRequiredName(IEnumerable<string> availableNames, IReadOnlyList<string> candidates)
        {
            var resolved = ResolveOptionalName(availableNames, candidates);
            if (resolved is null)
                throw new InvalidOperationException($"Required ONNX input missing. Candidates: {string.Join(", ", candidates)}");
            return resolved;
        }

        private static string? ResolveOptionalName(IEnumerable<string> availableNames, IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                var found = availableNames.FirstOrDefault(n =>
                    string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                    return found;
            }

            return null;
        }

        private static string ResolveOutputName(IEnumerable<string> availableNames, string preferredName)
        {
            var preferred = availableNames.FirstOrDefault(n =>
                string.Equals(n, preferredName, StringComparison.OrdinalIgnoreCase));
            return preferred ?? availableNames.First();
        }
    }
}
