namespace LiveLingo.Core.Models;

/// <summary>
/// Executes a translation <see cref="TranslationRoutePlan"/> with runtime fallback
/// across candidates. This is the single orchestration point for hybrid translation:
/// provider failures, first-token timeouts, and post-run quality-guard rejections all
/// funnel into the same "try next candidate" loop.
///
/// This interface replaces the ad-hoc retry logic that previously lived inside
/// <see cref="TranslationChatClient"/>'s TryRetryWithCloudAsync helper. Callers that
/// want fallback behaviour should build a request per candidate (message templates
/// may differ per chat template) via the <paramref name="requestBuilder"/> delegate.
/// </summary>
public interface ITranslationInvoker
{
    /// <summary>
    /// Non-streaming invocation with fallback. Each candidate is tried in order.
    /// Exceptions (including <see cref="OperationCanceledException"/> that are not
    /// user-triggered) and post-run quality-guard rejections promote the next
    /// candidate. The final attempt's failure is rethrown if every candidate fails.
    /// </summary>
    Task<TranslationInvocationOutcome> InvokeAsync(
        TranslationRoutePlan plan,
        Func<TranslationRouteCandidate, ModelInvocationRequest> requestBuilder,
        TranslationQualityAssertion? qualityGuard,
        CancellationToken ct = default);

    /// <summary>
    /// Streaming invocation with "pre-first-token" fallback. Until the first delta
    /// is yielded to the caller, a failure or first-token budget timeout switches
    /// to the next candidate. After the first delta is emitted further failures
    /// are propagated directly — downstream UI has already rendered partial text
    /// and silently switching providers would cause content flicker.
    /// </summary>
    IAsyncEnumerable<TranslationStreamingUpdate> InvokeStreamingAsync(
        TranslationRoutePlan plan,
        Func<TranslationRouteCandidate, ModelInvocationRequest> requestBuilder,
        TranslationQualityAssertion? qualityGuard,
        CancellationToken ct = default);
}

/// <summary>
/// Optional callback invoked with the final assembled translation. Returning
/// <c>false</c> marks the candidate as failed and triggers fallback to the next
/// candidate (for non-streaming) or a synthetic replacement update (for streaming).
/// </summary>
public delegate bool TranslationQualityAssertion(string translatedText);

public sealed record TranslationInvocationOutcome(
    TranslationRouteCandidate Candidate,
    string Text);

/// <summary>
/// Streaming update emitted by <see cref="ITranslationInvoker.InvokeStreamingAsync"/>.
/// Most updates carry the incremental <see cref="Delta"/> for the active candidate.
/// When a post-stream quality assertion fails and a non-streaming fallback produced
/// a replacement text, <see cref="ReplaceAll"/> is set and <see cref="Delta"/>
/// contains the full replacement string — the consumer should overwrite any text
/// it has shown so far with this payload.
/// </summary>
public sealed record TranslationStreamingUpdate(
    TranslationRouteCandidate Candidate,
    string Delta,
    bool ReplaceAll = false);
