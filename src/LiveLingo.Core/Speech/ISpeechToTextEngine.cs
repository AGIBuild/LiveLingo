namespace LiveLingo.Core.Speech;

public interface ISpeechToTextEngine : IDisposable
{
    Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        string? language = null,
        CancellationToken ct = default);
}
