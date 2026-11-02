namespace Dictation.Core;

public static class AudioFormat
{
    public const int SampleRate = 16000;

    public static double SamplesToMs(long samples) => samples * 1000.0 / SampleRate;

    public static long MsToSamples(double ms) => (long)(ms * SampleRate / 1000.0);
}

/// <summary>Half-open range of speech, in absolute samples since the session started.</summary>
public readonly record struct SpeechSpan(long StartSample, long EndSample);

/// <summary>Finds where speech ends so the stream can be cut inside silence.</summary>
public interface IVoiceActivityDetector : IDisposable
{
    void Accept(ReadOnlySpan<float> samples);

    /// <summary>
    /// True while the audio accepted so far ends inside speech. A span is only reported after it ended,
    /// so without this a pause cannot be told from speech that is still in progress.
    /// </summary>
    bool IsSpeechActive { get; }

    /// <summary>Takes the next span. A span is reported only after trailing silence was observed.</summary>
    bool TryTake(out SpeechSpan span);

    /// <summary>Forces any speech still in progress to be reported as a span.</summary>
    void Flush();
}

/// <summary>Streaming recognizer for the segment currently being spoken.</summary>
public interface ILiveRecognizer : IDisposable
{
    void Accept(ReadOnlySpan<float> samples);

    /// <summary>Best hypothesis for the open segment, including punctuation.</summary>
    string CurrentText { get; }

    /// <summary>Flushes right context, returns the final text of the open segment and starts a new one.</summary>
    string CloseSegment();
}

/// <summary>Offline recognizer used to re-decode one closed segment.</summary>
public interface ISegmentRecognizer : IDisposable
{
    string Recognize(ReadOnlySpan<float> samples);
}

/// <param name="Target">The only text the cleaner may rewrite.</param>
/// <param name="Preceding">Read-only context already written before the target.</param>
/// <param name="Continues">
/// Whether the speaker went on speaking after the target. Only the fact is given, never the words:
/// a small model copies read-only text into its output, and a partial hypothesis changes from moment to moment.
/// </param>
public sealed record CleanupRequest(
    string Target,
    string Preceding,
    bool Continues,
    IReadOnlyList<string> Glossary);

public interface ITextCleaner
{
    Task<string> CleanAsync(CleanupRequest request, CancellationToken ct);
}
