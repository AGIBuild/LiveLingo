namespace LiveLingo.Core.Transcription;

public interface IAudioFileTranscriber
{
    /// <summary>
    /// Recognizes a recording on this machine (slow: models load, then the audio is decoded at a multiple of real time);
    /// call it off the UI thread. The glossary is given to the recognizer as hotwords.
    /// </summary>
    /// <exception cref="Models.ModelNotReadyException">A speech model is not installed.</exception>
    /// <exception cref="UnsupportedAudioException">The file is not an uncompressed WAV file.</exception>
    Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct);
}
