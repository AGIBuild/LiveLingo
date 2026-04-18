using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels;

/// <summary>
/// Presentation projection of a <see cref="TranslationRouteTrace"/>. Pre-formats
/// every visible field so the XAML can stay dumb and binding-only — no converters,
/// no behind-the-scenes computation at render time. Construction is O(attempts).
/// </summary>
public sealed class TranslationTraceRow
{
    public TranslationTraceRow(TranslationRouteTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        Trace = trace;
        TimeLabel = trace.StartedAt.ToLocalTime().ToString("HH:mm:ss");
        DurationLabel = FormatDuration(trace.TotalDuration);
        KindLabel = trace.Kind == TranslationRouteInvocationKind.Streaming ? "stream" : "once";
        UsedFallback = trace.UsedFallback;
        (Glyph, GlyphBrushKey, OutcomeCaption) = OutcomePresentation(trace.Outcome);
        WinningCandidateLabel = trace.WinningCandidate is { } win
            ? FormatCandidate(win)
            : "—";
        Attempts = trace.Attempts
            .Select((a, index) => new TranslationAttemptRow(a, index + 1))
            .ToArray();
        StartedAtCaption = trace.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    }

    public TranslationRouteTrace Trace { get; }
    public string TimeLabel { get; }
    public string DurationLabel { get; }
    public string KindLabel { get; }
    public bool UsedFallback { get; }
    public string Glyph { get; }
    public string GlyphBrushKey { get; }
    public string OutcomeCaption { get; }
    public string WinningCandidateLabel { get; }
    public IReadOnlyList<TranslationAttemptRow> Attempts { get; }
    public string StartedAtCaption { get; }

    internal static string FormatCandidate(TranslationRouteCandidate candidate) =>
        $"{candidate.Tier.ToString().ToLowerInvariant()} · {candidate.Profile.DisplayName}";

    internal static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:F2}s"
            : $"{duration.TotalMilliseconds:F0}ms";

    private static (string glyph, string brushKey, string caption) OutcomePresentation(
        TranslationRouteTraceOutcome outcome) =>
        outcome switch
        {
            TranslationRouteTraceOutcome.Succeeded => ("●", "SuccessBrush", "ok"),
            TranslationRouteTraceOutcome.AllCandidatesFailed => ("✕", "DangerBrush", "fail"),
            TranslationRouteTraceOutcome.FailedAfterFirstToken => ("◐", "WarningBrush", "partial"),
            TranslationRouteTraceOutcome.Cancelled => ("○", "FgMutedBrush", "cancel"),
            _ => ("?", "FgMutedBrush", outcome.ToString())
        };
}

/// <summary>
/// Presentation projection of a single <see cref="TranslationRouteAttempt"/>.
/// </summary>
public sealed class TranslationAttemptRow
{
    public TranslationAttemptRow(TranslationRouteAttempt attempt, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Attempt = attempt;
        Header = $"#{ordinal} {TranslationTraceRow.FormatCandidate(attempt.Candidate)}";
        DurationLabel = TranslationTraceRow.FormatDuration(attempt.Duration);
        FirstTokenLabel = attempt.FirstTokenLatency is { } ftl
            ? $"ttf {TranslationTraceRow.FormatDuration(ftl)}"
            : string.Empty;
        HasFirstTokenLatency = attempt.FirstTokenLatency is not null;
        (Glyph, BrushKey, OutcomeCaption) = AttemptPresentation(attempt.Outcome);
        FailureReason = attempt.FailureReason ?? string.Empty;
        HasFailureReason = !string.IsNullOrWhiteSpace(attempt.FailureReason);
    }

    public TranslationRouteAttempt Attempt { get; }
    public string Header { get; }
    public string DurationLabel { get; }
    public string FirstTokenLabel { get; }
    public bool HasFirstTokenLatency { get; }
    public string Glyph { get; }
    public string BrushKey { get; }
    public string OutcomeCaption { get; }
    public string FailureReason { get; }
    public bool HasFailureReason { get; }

    private static (string glyph, string brushKey, string caption) AttemptPresentation(
        TranslationRouteAttemptOutcome outcome) =>
        outcome switch
        {
            TranslationRouteAttemptOutcome.Succeeded => ("✓", "SuccessBrush", "ok"),
            TranslationRouteAttemptOutcome.FailedWithException => ("✕", "DangerBrush", "error"),
            TranslationRouteAttemptOutcome.FirstTokenBudgetExpired => ("⌛", "WarningBrush", "timeout"),
            TranslationRouteAttemptOutcome.RejectedByQualityGuard => ("⚠", "WarningBrush", "rejected"),
            TranslationRouteAttemptOutcome.FailedAfterFirstToken => ("◐", "WarningBrush", "partial"),
            TranslationRouteAttemptOutcome.Cancelled => ("○", "FgMutedBrush", "cancel"),
            _ => ("?", "FgMutedBrush", outcome.ToString())
        };
}
