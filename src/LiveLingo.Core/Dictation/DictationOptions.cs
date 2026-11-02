namespace LiveLingo.Core.Dictation;

/// <summary>Defaults validated in poc/voice-dictation (see docs/superpowers/specs/2026-10-29-voice-dictation-design.md).</summary>
public sealed class DictationOptions
{
    /// <summary>
    /// Silence after speech that ends an utterance and therefore a sentence, and resets the live stream.
    /// Shorter pauses (a comma, a breath) never reset the stream: a fresh stream starting mid-sentence recognizes badly.
    /// </summary>
    public int HardPauseMs { get; init; } = 800;

    /// <summary>An utterance this long is cut at the next short pause instead of waiting for a hard pause.</summary>
    public int SoftMaxUtteranceMs { get; init; } = 12_000;

    /// <summary>A tail this long becomes a sentence at the next segment even without sentence punctuation.</summary>
    public int MaxTailChars { get; init; } = 90;

    /// <summary>
    /// Silence after speech that ends a passage. Sentences are collected into passages because the recognizer cuts
    /// speech at every pause, so a single sentence is often half a thought and cannot be cleaned or translated well.
    /// </summary>
    public int PassagePauseMs { get; init; } = 3000;

    /// <summary>A passage this long ends at its next sentence boundary, so cleanup of a long monologue is not postponed.</summary>
    public int PassageMaxChars { get; init; } = 200;

    /// <summary>
    /// The live text appears this long after the speech it recognizes. Audio this far before the first text is kept
    /// for the second pass.
    /// </summary>
    public int SpeechOnsetMarginMs { get; init; } = 3000;

    /// <summary>Audio kept after the last speech evidence (detector span end or last live text change) for the second pass.</summary>
    public int SpeechOffsetMarginMs { get; init; } = 300;

    /// <summary>Audio kept around a segment when it is re-decoded by the second pass.</summary>
    public int SecondPassPaddingMs { get; init; } = 200;

    /// <summary>
    /// A second-pass result with more characters per second of audio than anyone speaks is a decoder repetition loop,
    /// not speech. Such a result is dropped and the live text is kept. Pure English letters reach about 15 per second.
    /// </summary>
    public double SecondPassMaxCharsPerSecond { get; init; } = 15;

    /// <summary>How long stopping waits for recognition to drain; segments still unsettled then keep their live text.</summary>
    public int StopTimeoutMs { get; init; } = 15_000;
}

/// <summary>What the session knows about one closed segment; used for diagnostics and the replay harness.</summary>
public sealed record SegmentRecord(
    int Id,
    SpeechSpan Speech,
    SpeechSpan Audio,
    bool EndsSentence,
    string LiveFinal,
    string? SettledText,
    bool SecondPassFailed);
