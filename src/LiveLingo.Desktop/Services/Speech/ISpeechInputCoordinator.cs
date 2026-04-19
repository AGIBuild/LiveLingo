using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Services.Speech;

public interface ISpeechInputCoordinator : IDisposable
{
    VoiceInputState State { get; }

    event Action<VoiceInputState>? StateChanged;

    /// <summary>
    /// Fired when a complete VAD-bounded segment has been transcribed. Each segment
    /// is final and immutable; subscribers must APPEND this text. Segments are bounded
    /// by either a detected speech pause or the per-segment maximum length safeguard.
    /// </summary>
    event Action<string>? SegmentCommitted;

    /// <summary>
    /// Fired with a transient preview of the in-progress (uncommitted) segment so the
    /// UI can render live transcription. Subscribers should REPLACE the active preview
    /// portion of the visible text. An empty string is fired right after a segment commits
    /// so the preview slot is cleared atomically.
    /// </summary>
    event Action<string>? PartialPreview;

    Task<SpeechInputResult> StartRecordingAsync(string? language = null, CancellationToken ct = default);

    /// <summary>
    /// Stops capture and finalizes the session. Before returning, any uncommitted tail
    /// audio is drained through <see cref="SegmentCommitted"/>. The returned
    /// <c>SpeechInputResult.Text</c> is the concatenated text of every segment produced
    /// during this session — kept for callers that prefer the one-shot value over the
    /// streaming events.
    /// </summary>
    Task<SpeechInputResult> StopAndTranscribeAsync(string? language = null, CancellationToken ct = default);
    Task<SpeechInputResult> EnsureSttModelAsync(IProgress<float>? progress = null, CancellationToken ct = default);
    void CancelCurrent();
}
