using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Platform;

public interface IAudioCaptureService : IDisposable
{
    bool IsRecording { get; }
    Task StartAsync(CancellationToken ct = default);
    Task<AudioCaptureResult> StopAsync(CancellationToken ct = default);
    Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a snapshot of the audio captured so far without stopping recording.
    /// Returns null if not currently recording or no data captured yet.
    /// </summary>
    AudioCaptureResult? GetCurrentBuffer();
}
