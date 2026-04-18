namespace LiveLingo.Core.Models;

/// <summary>
/// Post-hoc record of a single <see cref="ITranslationInvoker"/> invocation. Captures
/// every candidate attempted, why each one failed (if any), and how long it took.
/// Emitted on <see cref="ITranslationTelemetry.RouteCompleted"/> exactly once per
/// invocation — including cancellations and terminal failures — so downstream
/// diagnostics can rely on a "one invocation = one trace" invariant.
/// </summary>
public sealed record TranslationRouteTrace(
    DateTimeOffset StartedAt,
    TimeSpan TotalDuration,
    TranslationRouteInvocationKind Kind,
    TranslationRouteTraceOutcome Outcome,
    TranslationRouteCandidate? WinningCandidate,
    IReadOnlyList<TranslationRouteAttempt> Attempts)
{
    /// <summary>
    /// <c>true</c> when the invocation ultimately succeeded but only after at least
    /// one earlier candidate failed — i.e. runtime fallback actually kicked in.
    /// </summary>
    public bool UsedFallback =>
        Outcome == TranslationRouteTraceOutcome.Succeeded && Attempts.Count > 1;
}

/// <summary>
/// Per-candidate record inside a <see cref="TranslationRouteTrace"/>.
/// </summary>
public sealed record TranslationRouteAttempt(
    TranslationRouteCandidate Candidate,
    TranslationRouteAttemptOutcome Outcome,
    TimeSpan Duration,
    TimeSpan? FirstTokenLatency,
    string? FailureReason);

public enum TranslationRouteInvocationKind
{
    NonStreaming,
    Streaming
}

/// <summary>
/// Overall outcome of an invocation. <see cref="Succeeded"/> means the caller got
/// a usable result (possibly after fallback); all other values indicate a terminal
/// state the caller observed as an exception or a partial stream.
/// </summary>
public enum TranslationRouteTraceOutcome
{
    Succeeded,
    AllCandidatesFailed,
    FailedAfterFirstToken,
    Cancelled
}

/// <summary>
/// Per-attempt outcome within a trace. Values are intentionally fine-grained so
/// telemetry consumers can distinguish between "provider crashed" and "provider was
/// too slow" — both of which trigger fallback but have very different fixes.
/// </summary>
public enum TranslationRouteAttemptOutcome
{
    Succeeded,
    FailedWithException,
    FirstTokenBudgetExpired,
    RejectedByQualityGuard,
    FailedAfterFirstToken,
    Cancelled
}
