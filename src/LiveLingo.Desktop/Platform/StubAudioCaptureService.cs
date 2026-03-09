using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Platform;

public sealed class StubAudioCaptureService : IAudioCaptureService
{
    public bool IsRecording => false;

    public Task StartAsync(CancellationToken ct = default) =>
        Task.FromException(new PlatformNotSupportedException(
            "Audio capture is not available on this platform."));

    public Task<AudioCaptureResult> StopAsync(CancellationToken ct = default) =>
        Task.FromException<AudioCaptureResult>(new PlatformNotSupportedException(
            "Audio capture is not available on this platform."));

    public Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default) =>
        Task.FromResult(MicrophonePermissionState.Unknown);

    public AudioCaptureResult? GetCurrentBuffer() => null;

    public void Dispose() { }
}
