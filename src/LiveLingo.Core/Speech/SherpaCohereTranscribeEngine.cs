using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Speech-to-text engine backed by sherpa-onnx running the Cohere Transcribe 14-language int8
/// bundle. The recognizer is created lazily on the first transcription request — this avoids
/// loading ~1.6 GB of ONNX weights into memory until the user actually presses talk, while still
/// reusing one recognizer across requests so we pay the warm-up cost only once.
/// </summary>
internal sealed class SherpaCohereTranscribeEngine : ISpeechToTextEngine
{
    private const int RecognizerSampleRate = 16_000;

    private readonly IModelManager _modelManager;
    private readonly CoreOptions _coreOptions;
    private readonly ILogger<SherpaCohereTranscribeEngine>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    public IReadOnlyCollection<string> SupportedModelIds { get; } =
        [ModelRegistry.SherpaCohereTranscribe14LangInt8.Id];

    public SherpaCohereTranscribeEngine(
        IModelManager modelManager,
        CoreOptions coreOptions,
        ILogger<SherpaCohereTranscribeEngine>? logger = null)
    {
        _modelManager = modelManager;
        _coreOptions = coreOptions;
        _logger = logger;
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        string? language = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var samples = ConvertToFloatMono(audio);
        var recognizer = await EnsureRecognizerAsync(ct).ConfigureAwait(false);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var stream = recognizer.CreateStream();
            ApplyLanguageHint(stream, language);
            stream.AcceptWaveform(RecognizerSampleRate, samples);
            recognizer.Decode(stream);

            var result = stream.Result;
            var text = result.Text?.Trim() ?? string.Empty;
            // Cohere Transcribe is single-language per stream — the recognizer doesn't return a
            // detected language. Echo back the caller-supplied hint so downstream language-aware
            // post-processing keeps working.
            var lang = NormalizeLanguageCode(language);
            return new SpeechTranscriptionResult(text, lang, 1.0f);
        }, ct).ConfigureAwait(false);
    }

    private async Task<OfflineRecognizer> EnsureRecognizerAsync(CancellationToken ct)
    {
        if (_recognizer is { } cached)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recognizer is { } existing)
                return existing;

            var descriptor = ModelRegistry.SherpaCohereTranscribe14LangInt8;
            if (!_modelManager.HasAllExpectedLocalAssets(descriptor))
            {
                throw new InvalidOperationException(
                    $"STT model '{descriptor.Id}' is not fully installed. " +
                    "Trigger model download from settings or call EnsureSttModelAsync first.");
            }

            var modelDir = _modelManager.GetModelDirectory(descriptor.Id);
            var encoderPath = Path.Combine(modelDir, "encoder.int8.onnx");
            var decoderPath = Path.Combine(modelDir, "decoder.int8.onnx");
            var tokensPath = Path.Combine(modelDir, "tokens.txt");

            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.NumThreads = ResolveThreadCount();
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            config.ModelConfig.CohereTranscribe.Encoder = encoderPath;
            config.ModelConfig.CohereTranscribe.Decoder = decoderPath;
            // Cohere Transcribe ships built-in punctuation + inverse text normalization;
            // both are off by default and cost nothing at inference time, so always enable
            // them to deliver readable, formatted text to translation downstream.
            config.ModelConfig.CohereTranscribe.UsePunct = 1;
            config.ModelConfig.CohereTranscribe.UseItn = 1;

            _logger?.LogInformation(
                "Initializing sherpa-onnx Cohere Transcribe recognizer: encoder={Encoder}, decoder={Decoder}, tokens={Tokens}, threads={Threads}",
                encoderPath, decoderPath, tokensPath, config.ModelConfig.NumThreads);

            _recognizer = new OfflineRecognizer(config);
            return _recognizer;
        }
        finally
        {
            _gate.Release();
        }
    }

    private int ResolveThreadCount()
    {
        var configured = _coreOptions.InferenceThreads;
        if (configured > 0)
            return configured;
        return Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 4));
    }

    private static void ApplyLanguageHint(OfflineStream stream, string? language)
    {
        var code = NormalizeLanguageCode(language);
        if (code.Length == 0)
            return;
        stream.SetOption("language", code);
    }

    /// <summary>
    /// Cohere Transcribe accepts ISO-639-1 codes (en, zh, ja, …); strip BCP-47 region tags
    /// like <c>zh-Hans</c> → <c>zh</c> so downstream tooling can stay BCP-47.
    /// </summary>
    private static string NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;
        return language.Split('-')[0].Trim().ToLowerInvariant();
    }

    /// <summary>
    /// sherpa-onnx accepts mono float samples in [-1, 1] at the recognizer sample rate (16 kHz here).
    /// Pipeline currently captures 16 kHz mono PCM16, so this is a straight int16 → float conversion.
    /// </summary>
    private static float[] ConvertToFloatMono(AudioCaptureResult audio)
    {
        if (audio.SampleRate != RecognizerSampleRate)
        {
            throw new NotSupportedException(
                $"SherpaCohereTranscribeEngine expects {RecognizerSampleRate} Hz audio; received {audio.SampleRate} Hz. " +
                "Add a resampling stage before calling TranscribeAsync.");
        }

        if (audio.Channels != 1)
        {
            throw new NotSupportedException(
                $"SherpaCohereTranscribeEngine expects mono audio; received {audio.Channels} channels.");
        }

        var pcm = audio.PcmData;
        var sampleCount = pcm.Length / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recognizer?.Dispose();
        _recognizer = null;
        _gate.Dispose();
    }
}
