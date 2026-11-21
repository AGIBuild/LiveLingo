using LiveLingo.Core.Dictation;

namespace LiveLingo.Core.Transcription;

public interface IAudioFileTranscriber
{
    /// <summary>
    /// Recognizes a recording (local models or the user's configured cloud speech endpoint); call it off the UI thread.
    /// </summary>
    /// <exception cref="Models.ModelNotReadyException">A local speech model is not installed.</exception>
    /// <exception cref="UnsupportedAudioException">The file is not an uncompressed WAV file.</exception>
    Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct);

    Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, SpeechSessionRequest request, IProgress<double>? progress, CancellationToken ct) =>
        TranscribeAsync(path, request.Hotwords, progress, ct);
}
