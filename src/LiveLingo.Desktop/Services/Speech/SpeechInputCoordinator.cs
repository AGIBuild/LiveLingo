using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Desktop.Platform;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Speech;

public sealed class SpeechInputCoordinator : ISpeechInputCoordinator
{
    private static readonly TimeSpan VadPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FallbackTranscriptionInterval = TimeSpan.FromSeconds(5);
    private const int PartialWindowSeconds = 10;
    private const int FinalWindowSeconds = 30;
    private const int BytesPerSample = 2;

    private readonly IAudioCaptureService _audioCapture;
    private readonly ISpeechToTextEngine _sttEngine;
    private readonly IModelManager _modelManager;
    private readonly IVoiceActivityDetector _vadDetector;
    private readonly ILogger<SpeechInputCoordinator>? _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _sessionCts;
    private string? _recordingLanguage;
    private Task? _partialLoop;
    private VoiceActivityMonitor? _vadMonitor;

    public VoiceInputState State { get; private set; } = VoiceInputState.Idle;
    public event Action<VoiceInputState>? StateChanged;
    public event Action<string>? PartialTranscription;

    public SpeechInputCoordinator(
        IAudioCaptureService audioCapture,
        ISpeechToTextEngine sttEngine,
        IModelManager modelManager,
        IVoiceActivityDetector vadDetector,
        ILogger<SpeechInputCoordinator>? logger = null)
    {
        _audioCapture = audioCapture;
        _sttEngine = sttEngine;
        _modelManager = modelManager;
        _vadDetector = vadDetector;
        _logger = logger;
    }

    public async Task<SpeechInputResult> StartRecordingAsync(string? language = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (State == VoiceInputState.Recording)
                return new SpeechInputResult(false, null, SpeechInputErrorCode.AlreadyRecording);
            if (State == VoiceInputState.Transcribing)
                return new SpeechInputResult(false, null, SpeechInputErrorCode.AlreadyRecording,
                    "Transcription in progress.");
        }

        var permission = await _audioCapture.GetPermissionStateAsync(ct);
        if (permission == MicrophonePermissionState.Denied ||
            permission == MicrophonePermissionState.Restricted)
        {
            SetState(VoiceInputState.Error);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.PermissionDenied,
                "Microphone permission is required.");
        }

        var sttModel = ModelRegistry.AllModels
            .FirstOrDefault(m => m.Type == ModelType.SpeechToText);
        if (sttModel is not null)
        {
            var installed = _modelManager.ListInstalled();
            if (!installed.Any(m => m.Id == sttModel.Id))
            {
                SetState(VoiceInputState.Error);
                return new SpeechInputResult(false, null, SpeechInputErrorCode.ModelMissing,
                    "STT model is not installed. Please download it first.");
            }
        }

        try
        {
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _recordingLanguage = language;
            await _audioCapture.StartAsync(_sessionCts.Token);
            SetState(VoiceInputState.Recording);
            _partialLoop = RunVadDrivenTranscriptionLoopAsync(_sessionCts.Token);
            return new SpeechInputResult(true, null, SpeechInputErrorCode.None);
        }
        catch (PlatformNotSupportedException)
        {
            SetState(VoiceInputState.Error);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.PlatformNotSupported,
                "Audio capture is not supported on this platform.");
        }
        catch (OperationCanceledException)
        {
            SetState(VoiceInputState.Idle);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.Cancelled);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start recording");
            SetState(VoiceInputState.Error);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed,
                ex.Message);
        }
    }

    public async Task<SpeechInputResult> StopAndTranscribeAsync(string? language = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (State != VoiceInputState.Recording)
                return new SpeechInputResult(false, null, SpeechInputErrorCode.NotRecording);
        }

        try
        {
            _sessionCts?.Cancel();
            SetState(VoiceInputState.Transcribing);

            if (_partialLoop is not null)
            {
                try { await _partialLoop; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger?.LogWarning(ex, "Partial loop ended with error"); }
            }
            _partialLoop = null;

            _vadMonitor?.Reset();
            _vadMonitor = null;

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var audio = await _audioCapture.StopAsync(_sessionCts.Token);

            var lang = language ?? _recordingLanguage;
            var windowedAudio = CreateWindow(audio, FinalWindowSeconds);
            var result = await _sttEngine.TranscribeAsync(windowedAudio, lang, _sessionCts.Token);
            SetState(VoiceInputState.Idle);
            return new SpeechInputResult(true, result.Text, SpeechInputErrorCode.None);
        }
        catch (OperationCanceledException)
        {
            SetState(VoiceInputState.Idle);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.Cancelled);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Transcription failed");
            SetState(VoiceInputState.Error);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed,
                ex.Message);
        }
    }

    private async Task RunVadDrivenTranscriptionLoopAsync(CancellationToken ct)
    {
        var pauseDetected = false;
        var lastTranscribeTime = DateTime.UtcNow;
        var lastProcessedBytes = 0;

        try
        {
            _vadDetector.Reset();
            _vadMonitor = new VoiceActivityMonitor(_vadDetector);
            _vadMonitor.SpeechPauseDetected += () => pauseDetected = true;

            while (!ct.IsCancellationRequested && State == VoiceInputState.Recording)
            {
                await Task.Delay(VadPollInterval, ct);

                var buffer = _audioCapture.GetCurrentBuffer();
                if (buffer is null || buffer.PcmData.Length <= lastProcessedBytes)
                    continue;

                var newBytes = buffer.PcmData.Length - lastProcessedBytes;
                var newSamples = ConvertPcmToFloat(buffer.PcmData, lastProcessedBytes, newBytes);
                lastProcessedBytes = buffer.PcmData.Length;

                try
                {
                    _vadMonitor.ProcessSamples(newSamples, newSamples.Length);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "VAD processing failed, skipping frame");
                    continue;
                }

                var timeSinceLastTranscribe = DateTime.UtcNow - lastTranscribeTime;
                var shouldTranscribe = pauseDetected ||
                                       timeSinceLastTranscribe >= FallbackTranscriptionInterval;

                if (!shouldTranscribe) continue;

                pauseDetected = false;
                lastTranscribeTime = DateTime.UtcNow;

                try
                {
                    var windowBuffer = CreateWindow(buffer, PartialWindowSeconds);
                    var result = await _sttEngine.TranscribeAsync(windowBuffer, _recordingLanguage, ct);
                    if (!string.IsNullOrWhiteSpace(result.Text))
                        PartialTranscription?.Invoke(result.Text);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Partial transcription failed (non-fatal)");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop/cancel
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "VAD transcription loop terminated unexpectedly");
        }
    }

    private static AudioCaptureResult CreateWindow(AudioCaptureResult full, int windowSeconds)
    {
        var maxWindowBytes = windowSeconds * full.SampleRate * full.Channels * BytesPerSample;
        if (full.PcmData.Length <= maxWindowBytes)
            return full;

        var windowStart = full.PcmData.Length - maxWindowBytes;
        var windowPcm = new byte[maxWindowBytes];
        Buffer.BlockCopy(full.PcmData, windowStart, windowPcm, 0, maxWindowBytes);
        var duration = TimeSpan.FromSeconds(
            (double)maxWindowBytes / (full.SampleRate * full.Channels * BytesPerSample));
        return new AudioCaptureResult(windowPcm, full.SampleRate, full.Channels, duration);
    }

    private static float[] ConvertPcmToFloat(byte[] pcm, int byteOffset, int byteCount)
    {
        var sampleCount = byteCount / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(pcm, byteOffset + i * 2);
            samples[i] = sample / 32768f;
        }
        return samples;
    }

    public async Task<SpeechInputResult> EnsureSttModelAsync(
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var sttModel = ModelRegistry.AllModels
            .FirstOrDefault(m => m.Type == ModelType.SpeechToText);
        if (sttModel is null)
            return new SpeechInputResult(false, null, SpeechInputErrorCode.ModelMissing,
                "No STT model defined in registry.");

        try
        {
            var downloadProgress = progress is not null
                ? new Progress<ModelDownloadProgress>(p =>
                    progress.Report(p.TotalBytes > 0
                        ? (float)p.BytesDownloaded / p.TotalBytes
                        : 0f))
                : null;

            await _modelManager.EnsureModelAsync(sttModel, downloadProgress, ct);

            var vadModel = ModelRegistry.AllModels
                .FirstOrDefault(m => m.Type == ModelType.VoiceActivityDetection);
            if (vadModel is not null)
                await _modelManager.EnsureModelAsync(vadModel, null, ct);

            return new SpeechInputResult(true, null, SpeechInputErrorCode.None);
        }
        catch (OperationCanceledException)
        {
            return new SpeechInputResult(false, null, SpeechInputErrorCode.Cancelled);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "STT model download failed");
            return new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed,
                ex.Message);
        }
    }

    public void CancelCurrent()
    {
        _sessionCts?.Cancel();
        if (_audioCapture.IsRecording)
        {
            try { _audioCapture.StopAsync().GetAwaiter().GetResult(); }
            catch { /* best-effort cleanup */ }
        }

        _vadMonitor?.Reset();
        _vadMonitor = null;
        SetState(VoiceInputState.Idle);
    }

    public void Dispose()
    {
        CancelCurrent();
        _sessionCts?.Dispose();
    }

    private void SetState(VoiceInputState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
