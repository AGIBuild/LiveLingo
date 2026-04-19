using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Shared lifecycle / audio-conversion plumbing for every offline sherpa-onnx ASR engine.
/// Each concrete engine only needs to declare the descriptor it serves and fill in the
/// model-specific bits of <see cref="OfflineRecognizerConfig"/>; threading, lazy init,
/// PCM16 → float conversion and disposal are handled here so the implementations stay
/// 1:1 with their model architecture without copy-pasting glue.
/// </summary>
internal abstract class SherpaOfflineRecognizerEngineBase : ISpeechToTextEngine
{
    protected const int RecognizerSampleRate = 16_000;

    private readonly IModelManager _modelManager;
    private readonly CoreOptions _coreOptions;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    protected SherpaOfflineRecognizerEngineBase(
        IModelManager modelManager,
        CoreOptions coreOptions,
        ILogger? logger)
    {
        _modelManager = modelManager;
        _coreOptions = coreOptions;
        _logger = logger;
    }

    public abstract IReadOnlyCollection<string> SupportedModelIds { get; }

    /// <summary>The sherpa-onnx model bundle this engine can serve.</summary>
    protected abstract ModelDescriptor Descriptor { get; }

    /// <summary>
    /// Fill in <see cref="OfflineRecognizerConfig.ModelConfig"/> with the model-specific paths
    /// (encoder/decoder/single-file model, language hints, ITN/punct flags, etc). The base class
    /// has already populated <c>Tokens</c>, <c>NumThreads</c>, <c>Provider</c> and <c>Debug</c>.
    /// <para>
    /// <b>Must be <see langword="ref"/>:</b> <see cref="OfflineRecognizerConfig"/> and every nested
    /// model config are <see langword="struct"/>s (sherpa-onnx marshals them straight to native).
    /// Without <see langword="ref"/> the implementation would mutate a copy and the native side
    /// would see empty model paths — causing
    /// <c>offline-recognizer-impl.cc:Create:320 Please provide a model</c>.
    /// </para>
    /// </summary>
    protected abstract void ConfigureRecognizer(ref OfflineRecognizerConfig config, string modelDir);

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
            var detectedLang = ResolveDetectedLanguage(result, language);
            return new SpeechTranscriptionResult(text, detectedLang, 1.0f);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Per-stream language override. Default no-op; engines that accept a per-utterance hint
    /// (e.g. Cohere Transcribe) override and call <c>stream.SetOption("language", code)</c>.
    /// </summary>
    protected virtual void ApplyLanguageHint(OfflineStream stream, string? language)
    {
    }

    /// <summary>
    /// Resolves the language code returned to the caller. Default echoes the caller-supplied
    /// hint normalised to ISO-639-1; engines with on-model language detection (e.g. SenseVoice
    /// running in <c>auto</c> mode) override to return <see cref="OfflineRecognizerResult.Lang"/>.
    /// </summary>
    protected virtual string ResolveDetectedLanguage(OfflineRecognizerResult result, string? hint)
        => NormalizeLanguageCode(hint);

    private async Task<OfflineRecognizer> EnsureRecognizerAsync(CancellationToken ct)
    {
        if (_recognizer is { } cached)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_recognizer is { } existing)
                return existing;

            var descriptor = Descriptor;
            if (!_modelManager.HasAllExpectedLocalAssets(descriptor))
            {
                throw new InvalidOperationException(
                    $"STT model '{descriptor.Id}' is not fully installed. " +
                    "Trigger model download from settings or call EnsureSttModelAsync first.");
            }

            var modelDir = _modelManager.GetModelDirectory(descriptor.Id);
            var config = BuildRecognizerConfig(modelDir);

            _logger?.LogInformation(
                "Initializing sherpa-onnx recognizer '{Id}' from {Dir} with {Threads} threads.",
                descriptor.Id, modelDir, config.ModelConfig.NumThreads);

            _recognizer = new OfflineRecognizer(config);
            return _recognizer;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Builds the fully-populated <see cref="OfflineRecognizerConfig"/> that would be handed to
    /// the native recognizer for a given on-disk model directory. Exposed as
    /// <c>internal</c> so unit tests can verify model-specific paths are wired up correctly
    /// without touching the native binary — the previous regression (struct-by-value loss of
    /// <c>CohereTranscribe.Encoder/Decoder</c>) only surfaced as a native crash, which means
    /// any change to <see cref="ConfigureRecognizer"/> needs a fast-feedback assertion at this
    /// layer.
    /// </summary>
    internal OfflineRecognizerConfig BuildRecognizerConfig(string modelDir)
    {
        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Tokens = Path.Combine(modelDir, "tokens.txt");
        config.ModelConfig.NumThreads = ResolveThreadCount();
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.Debug = 0;
        ConfigureRecognizer(ref config, modelDir);
        return config;
    }

    private int ResolveThreadCount()
    {
        var configured = _coreOptions.InferenceThreads;
        if (configured > 0)
            return configured;
        return Math.Max(1, Math.Min(Environment.ProcessorCount / 2, 4));
    }

    /// <summary>
    /// sherpa-onnx accepts ISO-639-1 codes (en, zh, ja, …); strip BCP-47 region tags
    /// like <c>zh-Hans</c> → <c>zh</c> so downstream tooling can stay BCP-47.
    /// </summary>
    protected static string NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;
        return language.Split('-')[0].Trim().ToLowerInvariant();
    }

    /// <summary>
    /// sherpa-onnx accepts mono float samples in [-1, 1] at the recognizer sample rate (16 kHz).
    /// LiveLingo's capture pipeline is already 16 kHz mono PCM16, so this is a straight int16 → float scale.
    /// </summary>
    protected static float[] ConvertToFloatMono(AudioCaptureResult audio)
    {
        if (audio.SampleRate != RecognizerSampleRate)
        {
            throw new NotSupportedException(
                $"Sherpa offline engines expect {RecognizerSampleRate} Hz audio; received {audio.SampleRate} Hz. " +
                "Add a resampling stage before calling TranscribeAsync.");
        }

        if (audio.Channels != 1)
        {
            throw new NotSupportedException(
                $"Sherpa offline engines expect mono audio; received {audio.Channels} channels.");
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
