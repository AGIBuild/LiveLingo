using System.Text;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Desktop.Platform;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Speech;

/// <summary>
/// VAD-driven streaming STT coordinator that converts the raw microphone capture
/// into an append-only transcript via the segment-commit pattern:
/// <list type="bullet">
///   <item>Each VAD-bounded utterance (or 60s safeguard) becomes one immutable
///         segment, transcribed exactly once, then surfaced via
///         <see cref="SegmentCommitted"/> for the UI to APPEND.</item>
///   <item>While a segment is in flight, a best-effort sliding preview window is
///         transcribed at most every 1.5s and surfaced via
///         <see cref="PartialPreview"/> for the UI to REPLACE its preview slot
///         (cleared atomically when the segment commits).</item>
/// </list>
/// All STT calls go through a single <see cref="SemaphoreSlim"/> so the underlying
/// recognizer is never invoked concurrently — previews skip themselves when a real
/// commit holds the gate, ensuring final accuracy is never starved by previews.
/// </summary>
public sealed class SpeechInputCoordinator : ISpeechInputCoordinator
{
    private static readonly TimeSpan VadPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PreviewMinInterval = TimeSpan.FromMilliseconds(1500);
    private const int MaxSegmentSeconds = 60;
    private const int PreviewWindowSeconds = 8;
    private const double MinSegmentSeconds = 0.4;
    private const int BytesPerSample = 2;

    private readonly IAudioCaptureService _audioCapture;
    private readonly ISpeechEngineSelector _engineSelector;
    private readonly IModelManager _modelManager;
    private readonly IModelDownloadCoordinator _downloadCoordinator;
    private readonly IVoiceActivityDetector _vadDetector;
    private readonly ILogger<SpeechInputCoordinator>? _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sttGate = new(1, 1);
    private CancellationTokenSource? _sessionCts;
    private string? _recordingLanguage;
    private Task? _vadLoop;
    private VoiceActivityMonitor? _vadMonitor;
    private int _committedByteOffset;
    private DateTime _lastPreviewAt = DateTime.MinValue;
    private readonly StringBuilder _sessionTranscript = new();

    public VoiceInputState State { get; private set; } = VoiceInputState.Idle;
    public event Action<VoiceInputState>? StateChanged;
    public event Action<string>? SegmentCommitted;
    public event Action<string>? PartialPreview;

    public SpeechInputCoordinator(
        IAudioCaptureService audioCapture,
        ISpeechEngineSelector engineSelector,
        IModelManager modelManager,
        IVoiceActivityDetector vadDetector,
        IModelDownloadCoordinator downloadCoordinator,
        ILogger<SpeechInputCoordinator>? logger = null)
    {
        _audioCapture = audioCapture;
        _engineSelector = engineSelector;
        _modelManager = modelManager;
        _downloadCoordinator = downloadCoordinator;
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

        var sttModel = _engineSelector.GetActiveModel();
        var installed = _modelManager.ListInstalled();
        if (!installed.Any(m => m.Id == sttModel.Id) ||
            !_modelManager.HasAllExpectedLocalAssets(sttModel))
        {
            SetState(VoiceInputState.Error);
            return new SpeechInputResult(false, null, SpeechInputErrorCode.ModelMissing,
                $"STT model '{sttModel.DisplayName}' is not installed. Please download it first.");
        }

        try
        {
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _recordingLanguage = language;
            _committedByteOffset = 0;
            _lastPreviewAt = DateTime.MinValue;
            _sessionTranscript.Clear();
            await _audioCapture.StartAsync(_sessionCts.Token);
            SetState(VoiceInputState.Recording);
            _vadLoop = RunVadDrivenTranscriptionLoopAsync(_sessionCts.Token);
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

            if (_vadLoop is not null)
            {
                try { await _vadLoop; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger?.LogWarning(ex, "VAD loop ended with error"); }
            }
            _vadLoop = null;

            _vadMonitor?.Reset();
            _vadMonitor = null;

            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var audio = await _audioCapture.StopAsync(_sessionCts.Token);

            var lang = language ?? _recordingLanguage;

            // Drain the uncommitted tail through the same SegmentCommitted channel
            // so the UI's append model sees a consistent stream of segments.
            await CommitTailIfAnyAsync(audio, lang, _sessionCts.Token).ConfigureAwait(false);

            // PartialPreview is cleared so subscribers don't keep a stale preview.
            PartialPreview?.Invoke(string.Empty);
            SetState(VoiceInputState.Idle);
            return new SpeechInputResult(true, _sessionTranscript.ToString(), SpeechInputErrorCode.None);
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
        var lastVadProcessedBytes = 0;

        try
        {
            _vadDetector.Reset();
            _vadMonitor = new VoiceActivityMonitor(_vadDetector);
            _vadMonitor.SpeechPauseDetected += () => pauseDetected = true;

            while (!ct.IsCancellationRequested && State == VoiceInputState.Recording)
            {
                await Task.Delay(VadPollInterval, ct);

                var buffer = _audioCapture.GetCurrentBuffer();
                if (buffer is null || buffer.PcmData.Length <= lastVadProcessedBytes)
                    continue;

                var newBytes = buffer.PcmData.Length - lastVadProcessedBytes;
                var newSamples = ConvertPcmToFloat(buffer.PcmData, lastVadProcessedBytes, newBytes);
                lastVadProcessedBytes = buffer.PcmData.Length;

                try
                {
                    _vadMonitor.ProcessSamples(newSamples, newSamples.Length);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "VAD processing failed, skipping frame");
                    continue;
                }

                var endByte = buffer.PcmData.Length;
                var segmentSeconds = BytesToSeconds(endByte - _committedByteOffset, buffer);
                var maxReached = segmentSeconds >= MaxSegmentSeconds;
                var pauseReady = pauseDetected && segmentSeconds >= MinSegmentSeconds;

                if (pauseReady || maxReached)
                {
                    pauseDetected = false;
                    var startByte = _committedByteOffset;
                    _committedByteOffset = endByte;
                    var slice = SliceAudio(buffer, startByte, endByte);
                    await CommitSegmentAsync(slice, ct).ConfigureAwait(false);
                    continue;
                }

                // Best-effort live preview of the in-flight segment.
                var sinceLastPreview = DateTime.UtcNow - _lastPreviewAt;
                if (segmentSeconds >= MinSegmentSeconds && sinceLastPreview >= PreviewMinInterval)
                {
                    _lastPreviewAt = DateTime.UtcNow;
                    var previewSlice = ExtractPreviewSlice(buffer, _committedByteOffset, endByte);
                    await TryPreviewAsync(previewSlice, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop/cancel.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "VAD transcription loop terminated unexpectedly");
        }
    }

    private async Task CommitSegmentAsync(AudioCaptureResult slice, CancellationToken ct)
    {
        if (slice.PcmData.Length == 0) return;

        await _sttGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var engine = _engineSelector.GetEngine();
            var result = await engine.TranscribeAsync(slice, _recordingLanguage, ct).ConfigureAwait(false);
            var text = result.Text?.Trim() ?? string.Empty;
            if (text.Length == 0) return;

            if (_sessionTranscript.Length > 0)
                _sessionTranscript.Append(' ');
            _sessionTranscript.Append(text);

            // Order matters: clear the preview first so subscribers don't render
            // both committed text and a stale preview for the same audio.
            PartialPreview?.Invoke(string.Empty);
            SegmentCommitted?.Invoke(text);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Segment transcription failed (segment dropped)");
        }
        finally
        {
            _sttGate.Release();
        }
    }

    private async Task CommitTailIfAnyAsync(AudioCaptureResult audio, string? lang, CancellationToken ct)
    {
        if (audio.PcmData.Length <= _committedByteOffset) return;

        var tailSeconds = BytesToSeconds(audio.PcmData.Length - _committedByteOffset, audio);
        if (tailSeconds < MinSegmentSeconds) return;

        var startByte = _committedByteOffset;
        _committedByteOffset = audio.PcmData.Length;
        var slice = SliceAudio(audio, startByte, audio.PcmData.Length);

        await _sttGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var engine = _engineSelector.GetEngine();
            var result = await engine.TranscribeAsync(slice, lang, ct).ConfigureAwait(false);
            var text = result.Text?.Trim() ?? string.Empty;
            if (text.Length == 0) return;

            if (_sessionTranscript.Length > 0)
                _sessionTranscript.Append(' ');
            _sessionTranscript.Append(text);
            SegmentCommitted?.Invoke(text);
        }
        finally
        {
            _sttGate.Release();
        }
    }

    private async Task TryPreviewAsync(AudioCaptureResult slice, CancellationToken ct)
    {
        if (slice.PcmData.Length == 0) return;
        // Skip when a real commit holds the gate — accuracy beats preview latency.
        if (!await _sttGate.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            var engine = _engineSelector.GetEngine();
            var result = await engine.TranscribeAsync(slice, _recordingLanguage, ct).ConfigureAwait(false);
            var text = result.Text?.Trim() ?? string.Empty;
            if (text.Length > 0)
                PartialPreview?.Invoke(text);
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Preview transcription failed (suppressed)");
        }
        finally
        {
            _sttGate.Release();
        }
    }

    private static AudioCaptureResult SliceAudio(AudioCaptureResult full, int startByte, int endByte)
    {
        var len = endByte - startByte;
        var pcm = new byte[len];
        Buffer.BlockCopy(full.PcmData, startByte, pcm, 0, len);
        var duration = TimeSpan.FromSeconds(BytesToSeconds(len, full));
        return new AudioCaptureResult(pcm, full.SampleRate, full.Channels, duration);
    }

    private static AudioCaptureResult ExtractPreviewSlice(AudioCaptureResult buffer, int committedOffset, int endByte)
    {
        var bytesPerSecond = buffer.SampleRate * buffer.Channels * BytesPerSample;
        var maxPreviewBytes = PreviewWindowSeconds * bytesPerSecond;
        var actualStart = Math.Max(committedOffset, endByte - maxPreviewBytes);
        return SliceAudio(buffer, actualStart, endByte);
    }

    private static double BytesToSeconds(int byteCount, AudioCaptureResult buffer)
    {
        var bytesPerSecond = buffer.SampleRate * buffer.Channels * BytesPerSample;
        return bytesPerSecond <= 0 ? 0 : (double)byteCount / bytesPerSecond;
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
        var sttModel = _engineSelector.GetActiveModel();

        try
        {
            var sttResult = await EnsureModelViaCoordinatorAsync(sttModel, progress, ct).ConfigureAwait(false);
            if (!sttResult.Success) return sttResult;

            var vadModel = ModelRegistry.AllModels
                .FirstOrDefault(m => m.Type == ModelType.VoiceActivityDetection);
            if (vadModel is not null)
            {
                var vadResult = await EnsureModelViaCoordinatorAsync(vadModel, null, ct).ConfigureAwait(false);
                if (!vadResult.Success) return vadResult;
            }

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

    /// <summary>
    /// Routes the download through the global coordinator so progress/state is
    /// visible to every UI surface and concurrent calls collapse into a single
    /// download. The optional <paramref name="progress"/> reporter forwards the
    /// coordinator's percent updates to callers that want a local stream.
    /// </summary>
    private async Task<SpeechInputResult> EnsureModelViaCoordinatorAsync(
        ModelDescriptor descriptor,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var initial = _downloadCoordinator.GetState(descriptor.Id);
        if (initial.IsInstalled)
            return new SpeechInputResult(true, null, SpeechInputErrorCode.None);

        Action<ModelDownloadState>? handler = null;
        if (progress is not null)
        {
            handler = state =>
            {
                if (!string.Equals(state.ModelId, descriptor.Id, StringComparison.Ordinal)) return;
                if (state.Status == ModelDownloadStatus.Downloading)
                    progress.Report((float)(Math.Clamp(state.Percentage, 0, 100) / 100.0));
            };
            _downloadCoordinator.StateChanged += handler;
        }

        try
        {
            // StartAsync is dedup-safe — concurrent callers attach to the same
            // session. The local cancellation token only aborts our wait; the
            // global download keeps going for any other observer.
            var startTask = _downloadCoordinator.StartAsync(descriptor);

            if (ct.CanBeCanceled)
            {
                var ctTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var reg = ct.Register(() => ctTcs.TrySetResult());
                var winner = await Task.WhenAny(startTask, ctTcs.Task).ConfigureAwait(false);
                if (winner != startTask)
                    throw new OperationCanceledException(ct);
            }
            else
            {
                await startTask.ConfigureAwait(false);
            }

            var finalState = _downloadCoordinator.GetState(descriptor.Id);
            return finalState.Status switch
            {
                ModelDownloadStatus.Installed =>
                    new SpeechInputResult(true, null, SpeechInputErrorCode.None),
                ModelDownloadStatus.Cancelled =>
                    new SpeechInputResult(false, null, SpeechInputErrorCode.Cancelled),
                _ =>
                    new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed,
                        finalState.ErrorMessage ?? "Model download failed."),
            };
        }
        finally
        {
            if (handler is not null)
                _downloadCoordinator.StateChanged -= handler;
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
        _committedByteOffset = 0;
        _sessionTranscript.Clear();
        SetState(VoiceInputState.Idle);
    }

    public void Dispose()
    {
        CancelCurrent();
        _sessionCts?.Dispose();
        _sttGate.Dispose();
    }

    private void SetState(VoiceInputState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
