namespace LiveLingo.Core.Speech;

public interface ISpeechToTextEngine : IDisposable
{
    /// <summary>
    /// Model ids that this engine can serve. Used by <see cref="ISpeechEngineSelector"/> to
    /// route a descriptor → engine pair without each engine reaching back into the registry.
    /// </summary>
    IReadOnlyCollection<string> SupportedModelIds { get; }

    Task<SpeechTranscriptionResult> TranscribeAsync(
        AudioCaptureResult audio,
        string? language = null,
        CancellationToken ct = default);
}
