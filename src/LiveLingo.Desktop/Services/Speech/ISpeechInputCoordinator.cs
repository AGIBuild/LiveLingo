using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Services.Speech;

public interface ISpeechInputCoordinator : IDisposable
{
    VoiceInputState State { get; }

    event Action<VoiceInputState>? StateChanged;

    /// <summary>
    /// Fired during recording with partial transcription text from periodic audio snapshots.
    /// </summary>
    event Action<string>? PartialTranscription;

    Task<SpeechInputResult> StartRecordingAsync(string? language = null, CancellationToken ct = default);
    Task<SpeechInputResult> StopAndTranscribeAsync(string? language = null, CancellationToken ct = default);
    Task<SpeechInputResult> EnsureSttModelAsync(IProgress<float>? progress = null, CancellationToken ct = default);
    void CancelCurrent();
}
