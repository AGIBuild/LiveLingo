namespace LiveLingo.Core.Dictation;

/// <summary>Defaults validated in poc/voice-dictation (see docs/superpowers/specs/2026-10-29-voice-dictation-design.md).</summary>
public sealed class DictationOptions
{
    /// <summary>
    /// Silence after speech that ends an utterance and therefore a sentence, and resets the live stream.
    /// Shorter pauses (a comma, a breath, switching between Chinese and an English term) never reset the stream:
    /// a fresh stream starting mid-sentence recognizes badly.
    /// </summary>
    public int HardPauseMs { get; init; } = 800;

    /// <summary>An utterance this long is cut at the next short pause instead of waiting for a hard pause.</summary>
    public int SoftMaxUtteranceMs { get; init; } = 4_000;

    /// <summary>
    /// Upper bound on an open utterance when the speaker never pauses (or background noise keeps VAD active),
    /// sized well within Qwen3-ASR's 512-token total KV budget so live cleanup never waits over ten seconds for a slice.
    /// </summary>
    public int HardMaxUtteranceMs { get; init; } = 8_000;

    /// <summary>A tail this long becomes a sentence at the next segment even without sentence punctuation.</summary>
    public int MaxTailChars { get; init; } = 45;

    /// <summary>
    /// Silence after speech that ends a passage. Sentences are collected into compact micro-passages during live speech
    /// so cleanup previews stream promptly; stopping polishes neighbouring passages together as one coherent text.
    /// </summary>
    public int PassagePauseMs { get; init; } = 1200;

    /// <summary>A passage this long ends at its next sentence boundary, keeping live cleanup responsive (~1–2 sentences).</summary>
    public int PassageMaxChars { get; init; } = 55;

    /// <summary>
    /// Minimum length of the very first settled sentence in a session to emit immediately as its own passage without
    /// waiting for <see cref="PassageMaxChars"/> or <see cref="PassagePauseMs"/>, so the result pane starts cleaning
    /// right after the user's opening sentence. Set to 0 to disable fast-tracking.
    /// </summary>
    public int FirstPassageMinChars { get; init; } = 8;

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
    /// Also gates autoregressive phrase-repetition and truncated-collapse guards against the live transcript.
    /// Set to 0 or less for non-autoregressive models that cannot enter token repetition loops.
    /// </summary>
    public double SecondPassMaxCharsPerSecond { get; init; } = 15;

    /// <summary>
    /// Holds premature sentence-final periods emitted on isolated segments ending in dangling conjunctions or prepositions.
    /// Enabled for isolated local segment recognizers; disabled for full-context streaming models.
    /// </summary>
    public bool HoldDanglingClausePeriod { get; init; } = true;

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
