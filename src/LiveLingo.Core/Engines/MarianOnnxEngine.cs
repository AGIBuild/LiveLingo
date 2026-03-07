using System.Collections.Concurrent;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

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
        return session.Translate(text, ct);
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
            var modelDir = ((ModelManager)_modelManager).GetModelDirectory(descriptor.Id);

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
        private readonly InferenceSession _session;
        private readonly ILogger _logger;

        public ModelSession(string modelDir, ILogger logger)
        {
            _logger = logger;
            var modelPath = Path.Combine(modelDir, "model.onnx");
            var options = new SessionOptions { InterOpNumThreads = 2, IntraOpNumThreads = 2 };
            _session = new InferenceSession(modelPath, options);
            _logger.LogInformation("Loaded ONNX model from {Path}", modelPath);
        }

        public string Translate(string text, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // TODO P2-impl: implement encoder-decoder inference with SentencePiece tokenizer
            // placeholder that returns stub result until tokenizer is integrated
            _logger.LogDebug("Translating: {Text}", text);
            return $"[translated] {text}";
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
