using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace LiveLingo.Core.Speech;

public sealed class WhisperSpeechToTextEngine : ISpeechToTextEngine
{
    private readonly IModelManager _modelManager;
    private readonly ILogger<WhisperSpeechToTextEngine>? _logger;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelPath;
    private string? _loadedLanguage;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public WhisperSpeechToTextEngine(
        IModelManager modelManager,
        ILogger<WhisperSpeechToTextEngine>? logger = null)
    {
        _modelManager = modelManager;
        _logger = logger;
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        string? language = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var processor = await GetOrLoadProcessorAsync(language, ct);
        var samples = ConvertPcmToFloat(audio);

        var segments = new List<string>();
        string detectedLanguage = language ?? "en";

        await foreach (var segment in processor.ProcessAsync(samples, ct))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
                segments.Add(segment.Text.Trim());
            if (!string.IsNullOrEmpty(segment.Language))
                detectedLanguage = segment.Language;
        }

        var text = string.Join(" ", segments);
        _logger?.LogInformation(
            "Whisper transcription completed. Language={Language}, Length={Length}",
            detectedLanguage, text.Length);

        return new SpeechTranscriptionResult(text, detectedLanguage, 1.0f);
    }

    private async Task<WhisperProcessor> GetOrLoadProcessorAsync(string? language, CancellationToken ct)
    {
        var sttModel = ModelRegistry.AllModels
            .FirstOrDefault(m => m.Type == ModelType.SpeechToText)
            ?? throw new InvalidOperationException("No STT model defined in registry.");

        var modelDir = _modelManager.GetModelDirectory(sttModel.Id);
        var modelPath = Path.Combine(modelDir, Path.GetFileName(sttModel.DownloadUrl));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"STT model file not found at {modelPath}. Download it first.", modelPath);

        await _loadLock.WaitAsync(ct);
        try
        {
            var languageKey = language?.ToLowerInvariant();
            if (_processor is not null && _loadedModelPath == modelPath && _loadedLanguage == languageKey)
                return _processor;

            _processor?.Dispose();

            if (_factory is null || _loadedModelPath != modelPath)
            {
                _logger?.LogInformation("Loading Whisper model from {Path}", modelPath);
                _factory = WhisperFactory.FromPath(modelPath);
            }

            var builder = _factory.CreateBuilder();
            if (!string.IsNullOrWhiteSpace(language))
            {
                _logger?.LogDebug("Whisper language set to {Language}", language);
                builder.WithLanguage(language);
            }
            else
            {
                builder.WithLanguageDetection();
            }

            _processor = builder.Build();
            _loadedModelPath = modelPath;
            _loadedLanguage = languageKey;

            return _processor;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static float[] ConvertPcmToFloat(AudioCaptureResult audio)
    {
        if (audio.SampleRate != 16000 || audio.Channels != 1)
            throw new InvalidOperationException(
                $"Expected 16kHz mono PCM, got {audio.SampleRate}Hz {audio.Channels}ch.");

        var sampleCount = audio.PcmData.Length / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(audio.PcmData, i * 2);
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
        _loadLock.Dispose();
    }
}
