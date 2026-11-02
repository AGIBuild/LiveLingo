namespace Dictation.Core;

public enum SentenceState
{
    /// <summary>Settled text waiting for the cleaner.</summary>
    Pending,
    InFlight,
    Cleaned,
    /// <summary>The cleaner result failed the fidelity gate; the raw text is kept.</summary>
    Rejected,
    /// <summary>The user edited the sentence; background results must never overwrite it.</summary>
    UserOwned,
    /// <summary>No cleaner is configured, or the commit timed out before cleanup finished.</summary>
    Raw,
}

public sealed class Sentence
{
    public required int Id { get; init; }
    public required string Raw { get; init; }
    public required int EndSegmentId { get; init; }
    public required double FormedMs { get; init; }
    public string? Cleaned { get; set; }
    public string? UserText { get; set; }
    public SentenceState State { get; set; } = SentenceState.Pending;
    public double CleanedMs { get; set; } = -1;
    public double CleanupDurationMs { get; set; } = -1;
    public string? RejectReason { get; set; }

    /// <summary>What the cleaner produced when the gate rejected it; kept only for analysis, never shown.</summary>
    public string? RejectedText { get; set; }

    public string Best => UserText ?? Cleaned ?? Raw;
}

/// <summary>One acoustic segment: a stretch of speech between two pauses.</summary>
public sealed class Segment
{
    public required int Id { get; init; }

    /// <summary>Where the detector heard speech. Used for latency measurements, never to decide what gets decoded.</summary>
    public required SpeechSpan Speech { get; init; }

    /// <summary>The audio the live stream consumed for this segment, which is what the second pass decodes.</summary>
    public required SpeechSpan Audio { get; init; }

    /// <summary>True when the utterance ended at a hard pause, so its text ends a sentence.</summary>
    public required bool EndsSentence { get; init; }
    public required string LiveFinal { get; init; }
    public required double ClosedMs { get; init; }
    public double FirstTextMs { get; init; } = -1;
    public int LiveRevisions { get; init; }
    public int LiveRetractedChars { get; init; }

    /// <summary>Text that feeds sentence building; set when the live text is final or the second pass finished.</summary>
    public string? SettledText { get; set; }
    public double SettledMs { get; set; } = -1;
    public double SecondPassDurationMs { get; set; } = -1;
    public bool SecondPassFailed { get; set; }
}

public sealed record SentenceView(int Id, string Text, SentenceState State);

public sealed record DraftSnapshot(
    IReadOnlyList<SentenceView> Sentences,
    string Tail,
    string Unsettled,
    string Live)
{
    public string FullText
    {
        get
        {
            var text = "";
            foreach (var s in Sentences) text = SentenceSplitter.Join(text, s.Text);
            text = SentenceSplitter.Join(text, Tail);
            text = SentenceSplitter.Join(text, Unsettled);
            return SentenceSplitter.Join(text, Live);
        }
    }
}

public sealed record CommitResult(
    string Text,
    string RawText,
    string LiveText,
    int Sentences,
    int Cleaned,
    int Rejected,
    int RawOnly,
    double WaitMs,
    bool TimedOut);

public sealed record BacklogSample(double WallMs, double AudioBacklogMs);

public sealed record SessionReport(
    IReadOnlyList<Segment> Segments,
    IReadOnlyList<Sentence> Sentences,
    IReadOnlyList<BacklogSample> Backlog,
    CommitResult? Commit);
