namespace LiveLingo.Core.Speech;

public sealed class StubSpeechToTextEngine : ISpeechToTextEngine
{
    public Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        string? language = null,
        CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "No STT engine is configured. Install a speech-to-text model first.");
    }

    public void Dispose() { }
}
