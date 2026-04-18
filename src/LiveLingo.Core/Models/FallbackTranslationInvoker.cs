using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models;

/// <summary>
/// Default <see cref="ITranslationInvoker"/> that walks a <see cref="TranslationRoutePlan"/>
/// and tries each candidate in order. Designed to be the sole runtime-fallback
/// orchestrator so individual chat-client / post-processor code paths stay linear.
///
/// Every invocation — success, failure, or cancellation — emits exactly one
/// <see cref="TranslationRouteTrace"/> through <see cref="ITranslationTelemetry"/>,
/// giving downstream diagnostics a reliable "one translation = one trace" signal.
/// </summary>
public sealed class FallbackTranslationInvoker(
    IModelInvocationService invocationService,
    ITranslationTelemetry telemetry,
    ILogger<FallbackTranslationInvoker>? logger = null) : ITranslationInvoker
{
    public async Task<TranslationInvocationOutcome> InvokeAsync(
        TranslationRoutePlan plan,
        Func<TranslationRouteCandidate, ModelInvocationRequest> requestBuilder,
        TranslationQualityAssertion? qualityGuard,
        CancellationToken ct = default)
    {
        if (!plan.HasCandidates)
            throw new InvalidOperationException("Translation route plan has no candidates.");

        var startedAt = DateTimeOffset.UtcNow;
        var overallSw = Stopwatch.StartNew();
        var attempts = new List<TranslationRouteAttempt>(plan.Candidates.Count);
        TranslationRouteCandidate? winner = null;
        var traceOutcome = TranslationRouteTraceOutcome.AllCandidatesFailed;
        Exception? lastException = null;

        try
        {
            for (var i = 0; i < plan.Candidates.Count; i++)
            {
                var candidate = plan.Candidates[i];
                ct.ThrowIfCancellationRequested();

                var attemptSw = Stopwatch.StartNew();
                try
                {
                    var request = requestBuilder(candidate);
                    var result = await invocationService.InvokeAsync(request, ct).ConfigureAwait(false);
                    attemptSw.Stop();

                    if (qualityGuard is not null && !qualityGuard(result.Text))
                    {
                        logger?.LogWarning(
                            "Candidate {Tier}/{ProfileId} failed translation quality guard; attempting next route candidate.",
                            candidate.Tier, candidate.Profile.Id);
                        attempts.Add(new TranslationRouteAttempt(
                            candidate,
                            TranslationRouteAttemptOutcome.RejectedByQualityGuard,
                            attemptSw.Elapsed,
                            FirstTokenLatency: null,
                            FailureReason: "Quality guard rejected output."));
                        lastException = new InvalidOperationException(
                            $"Candidate {candidate.Profile.Id} produced output rejected by quality guard.");
                        continue;
                    }

                    attempts.Add(new TranslationRouteAttempt(
                        candidate,
                        TranslationRouteAttemptOutcome.Succeeded,
                        attemptSw.Elapsed,
                        FirstTokenLatency: null,
                        FailureReason: null));
                    winner = candidate;
                    traceOutcome = TranslationRouteTraceOutcome.Succeeded;
                    return new TranslationInvocationOutcome(candidate, result.Text);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    attemptSw.Stop();
                    attempts.Add(new TranslationRouteAttempt(
                        candidate,
                        TranslationRouteAttemptOutcome.Cancelled,
                        attemptSw.Elapsed,
                        FirstTokenLatency: null,
                        FailureReason: "Cancelled by caller."));
                    traceOutcome = TranslationRouteTraceOutcome.Cancelled;
                    throw;
                }
                catch (Exception ex)
                {
                    attemptSw.Stop();
                    lastException = ex;
                    attempts.Add(new TranslationRouteAttempt(
                        candidate,
                        TranslationRouteAttemptOutcome.FailedWithException,
                        attemptSw.Elapsed,
                        FirstTokenLatency: null,
                        FailureReason: FormatFailureReason(ex)));
                    logger?.LogWarning(
                        ex,
                        "Candidate {Tier}/{ProfileId} failed non-streaming translation; will try next candidate if any.",
                        candidate.Tier, candidate.Profile.Id);
                }
            }

            throw new InvalidOperationException(
                $"All {plan.Candidates.Count} translation route candidates failed.", lastException);
        }
        finally
        {
            overallSw.Stop();
            // `ct.ThrowIfCancellationRequested()` at the top of the loop can fire
            // before any candidate runs; in that path the inner catch never observes
            // the cancellation, so we classify it here instead of letting it look
            // like "all candidates failed".
            if (traceOutcome == TranslationRouteTraceOutcome.AllCandidatesFailed
                && ct.IsCancellationRequested)
            {
                traceOutcome = TranslationRouteTraceOutcome.Cancelled;
            }

            telemetry.Record(new TranslationRouteTrace(
                startedAt,
                overallSw.Elapsed,
                TranslationRouteInvocationKind.NonStreaming,
                traceOutcome,
                winner,
                attempts));
        }
    }

    public async IAsyncEnumerable<TranslationStreamingUpdate> InvokeStreamingAsync(
        TranslationRoutePlan plan,
        Func<TranslationRouteCandidate, ModelInvocationRequest> requestBuilder,
        TranslationQualityAssertion? qualityGuard,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!plan.HasCandidates)
            throw new InvalidOperationException("Translation route plan has no candidates.");

        var startedAt = DateTimeOffset.UtcNow;
        var overallSw = Stopwatch.StartNew();
        var attempts = new List<TranslationRouteAttempt>(plan.Candidates.Count);
        TranslationRouteCandidate? winner = null;
        var traceOutcome = TranslationRouteTraceOutcome.AllCandidatesFailed;

        try
        {
            var winningCandidateIndex = -1;
            string? assembledText = null;

            for (var i = 0; i < plan.Candidates.Count; i++)
            {
                var candidate = plan.Candidates[i];
                if (ct.IsCancellationRequested)
                {
                    traceOutcome = TranslationRouteTraceOutcome.Cancelled;
                    ct.ThrowIfCancellationRequested();
                }

                var assembler = new StringBuilder();
                var streamResult = await TryStreamCandidateAsync(
                    candidate, requestBuilder(candidate), assembler, ct).ConfigureAwait(false);

                attempts.Add(BuildStreamingAttempt(candidate, streamResult));

                if (streamResult.Deltas is { Count: > 0 })
                {
                    foreach (var delta in streamResult.Deltas)
                        yield return new TranslationStreamingUpdate(candidate, delta);
                }

                if (streamResult.Status == StreamAttemptStatus.Succeeded)
                {
                    winningCandidateIndex = i;
                    winner = candidate;
                    assembledText = assembler.ToString();
                    break;
                }

                if (streamResult.Status == StreamAttemptStatus.FailedAfterFirstToken)
                {
                    traceOutcome = TranslationRouteTraceOutcome.FailedAfterFirstToken;
                    throw new InvalidOperationException(
                        $"Streaming candidate {candidate.Profile.Id} failed after partial output was emitted.",
                        streamResult.Error);
                }

                logger?.LogWarning(
                    streamResult.Error,
                    "Candidate {Tier}/{ProfileId} failed before first token ({Reason}); falling back to next candidate.",
                    candidate.Tier, candidate.Profile.Id, streamResult.Status);
            }

            if (winningCandidateIndex < 0)
            {
                throw new InvalidOperationException(
                    $"All {plan.Candidates.Count} streaming translation candidates failed before producing output.");
            }

            if (qualityGuard is null || assembledText is null || qualityGuard(assembledText))
            {
                traceOutcome = TranslationRouteTraceOutcome.Succeeded;
                yield break;
            }

            logger?.LogWarning(
                "Streaming output from candidate {Tier}/{ProfileId} failed quality guard; attempting non-streaming replacement via remaining candidates.",
                plan.Candidates[winningCandidateIndex].Tier,
                plan.Candidates[winningCandidateIndex].Profile.Id);

            // The winning streaming attempt is being retroactively rejected by the
            // post-stream quality guard — rewrite its trace entry so telemetry reflects
            // the real outcome rather than "Succeeded".
            RetroactivelyRejectLastAttempt(attempts);

            var replacement = await TryProduceReplacementAsync(
                plan, winningCandidateIndex, requestBuilder, qualityGuard, attempts, ct).ConfigureAwait(false);
            if (replacement is not null)
            {
                winner = replacement.Candidate;
                traceOutcome = TranslationRouteTraceOutcome.Succeeded;
                yield return new TranslationStreamingUpdate(replacement.Candidate, replacement.Text, ReplaceAll: true);
            }
            else
            {
                winner = null;
                traceOutcome = TranslationRouteTraceOutcome.AllCandidatesFailed;
            }
        }
        finally
        {
            overallSw.Stop();
            // User cancellation may have bypassed any of the explicit traceOutcome
            // assignments above (e.g. through ConfigureAwait-captured cancellation);
            // detect that here so cancellations always show up as such in telemetry.
            if (traceOutcome == TranslationRouteTraceOutcome.AllCandidatesFailed
                && winner is null
                && ct.IsCancellationRequested)
            {
                traceOutcome = TranslationRouteTraceOutcome.Cancelled;
            }

            telemetry.Record(new TranslationRouteTrace(
                startedAt,
                overallSw.Elapsed,
                TranslationRouteInvocationKind.Streaming,
                traceOutcome,
                winner,
                attempts));
        }
    }

    private async Task<StreamAttemptResult> TryStreamCandidateAsync(
        TranslationRouteCandidate candidate,
        ModelInvocationRequest request,
        StringBuilder assembler,
        CancellationToken outerCt)
    {
        using var firstTokenCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        firstTokenCts.CancelAfter(candidate.FirstTokenBudget);
        var attemptSw = Stopwatch.StartNew();
        TimeSpan? firstTokenLatency = null;
        var hasYielded = false;
        var deltas = new List<string>();

        await using var enumerator = invocationService
            .InvokeStreamingAsync(request, firstTokenCts.Token)
            .GetAsyncEnumerator(firstTokenCts.Token);

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (outerCt.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (!hasYielded)
            {
                attemptSw.Stop();
                return new StreamAttemptResult(
                    StreamAttemptStatus.FirstTokenTimeout, deltas, ex,
                    attemptSw.Elapsed, firstTokenLatency);
            }
            catch (Exception ex) when (!hasYielded)
            {
                attemptSw.Stop();
                return new StreamAttemptResult(
                    StreamAttemptStatus.FailedBeforeFirstToken, deltas, ex,
                    attemptSw.Elapsed, firstTokenLatency);
            }
            catch (Exception ex)
            {
                attemptSw.Stop();
                return new StreamAttemptResult(
                    StreamAttemptStatus.FailedAfterFirstToken, deltas, ex,
                    attemptSw.Elapsed, firstTokenLatency);
            }

            if (!hasNext) break;

            var delta = enumerator.Current;
            if (string.IsNullOrEmpty(delta)) continue;

            if (!hasYielded)
            {
                firstTokenLatency = attemptSw.Elapsed;
                // First token arrived — cancel the first-token watchdog.
                // Subsequent reads run on the outer token only.
                firstTokenCts.CancelAfter(Timeout.InfiniteTimeSpan);
                hasYielded = true;
            }
            assembler.Append(delta);
            deltas.Add(delta);
        }

        attemptSw.Stop();
        return new StreamAttemptResult(
            StreamAttemptStatus.Succeeded, deltas, null,
            attemptSw.Elapsed, firstTokenLatency);
    }

    private async Task<TranslationInvocationOutcome?> TryProduceReplacementAsync(
        TranslationRoutePlan plan,
        int failedCandidateIndex,
        Func<TranslationRouteCandidate, ModelInvocationRequest> requestBuilder,
        TranslationQualityAssertion qualityGuard,
        List<TranslationRouteAttempt> attempts,
        CancellationToken ct)
    {
        for (var i = failedCandidateIndex + 1; i < plan.Candidates.Count; i++)
        {
            var candidate = plan.Candidates[i];
            ct.ThrowIfCancellationRequested();

            var attemptSw = Stopwatch.StartNew();
            try
            {
                var streamingRequest = requestBuilder(candidate);
                var request = streamingRequest with
                {
                    Options = streamingRequest.Options with { Stream = false }
                };
                var result = await invocationService.InvokeAsync(request, ct).ConfigureAwait(false);
                attemptSw.Stop();

                if (qualityGuard(result.Text))
                {
                    attempts.Add(new TranslationRouteAttempt(
                        candidate,
                        TranslationRouteAttemptOutcome.Succeeded,
                        attemptSw.Elapsed,
                        FirstTokenLatency: null,
                        FailureReason: null));
                    return new TranslationInvocationOutcome(candidate, result.Text);
                }

                attempts.Add(new TranslationRouteAttempt(
                    candidate,
                    TranslationRouteAttemptOutcome.RejectedByQualityGuard,
                    attemptSw.Elapsed,
                    FirstTokenLatency: null,
                    FailureReason: "Quality guard rejected replacement output."));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                attemptSw.Stop();
                attempts.Add(new TranslationRouteAttempt(
                    candidate,
                    TranslationRouteAttemptOutcome.Cancelled,
                    attemptSw.Elapsed,
                    FirstTokenLatency: null,
                    FailureReason: "Cancelled by caller."));
                throw;
            }
            catch (Exception ex)
            {
                attemptSw.Stop();
                attempts.Add(new TranslationRouteAttempt(
                    candidate,
                    TranslationRouteAttemptOutcome.FailedWithException,
                    attemptSw.Elapsed,
                    FirstTokenLatency: null,
                    FailureReason: FormatFailureReason(ex)));
                logger?.LogWarning(
                    ex,
                    "Replacement candidate {Tier}/{ProfileId} failed; continuing.",
                    candidate.Tier, candidate.Profile.Id);
            }
        }
        return null;
    }

    private static TranslationRouteAttempt BuildStreamingAttempt(
        TranslationRouteCandidate candidate,
        StreamAttemptResult streamResult)
    {
        var outcome = streamResult.Status switch
        {
            StreamAttemptStatus.Succeeded => TranslationRouteAttemptOutcome.Succeeded,
            StreamAttemptStatus.FirstTokenTimeout => TranslationRouteAttemptOutcome.FirstTokenBudgetExpired,
            StreamAttemptStatus.FailedBeforeFirstToken => TranslationRouteAttemptOutcome.FailedWithException,
            StreamAttemptStatus.FailedAfterFirstToken => TranslationRouteAttemptOutcome.FailedAfterFirstToken,
            _ => TranslationRouteAttemptOutcome.FailedWithException
        };
        var reason = streamResult.Status == StreamAttemptStatus.Succeeded
            ? null
            : streamResult.Status == StreamAttemptStatus.FirstTokenTimeout
                ? $"First-token budget of {candidate.FirstTokenBudget.TotalMilliseconds:0} ms expired."
                : streamResult.Error is { } ex
                    ? FormatFailureReason(ex)
                    : "Unknown streaming failure.";
        return new TranslationRouteAttempt(
            candidate,
            outcome,
            streamResult.Duration,
            streamResult.FirstTokenLatency,
            reason);
    }

    private static void RetroactivelyRejectLastAttempt(List<TranslationRouteAttempt> attempts)
    {
        if (attempts.Count == 0) return;
        var last = attempts[^1];
        if (last.Outcome != TranslationRouteAttemptOutcome.Succeeded) return;
        attempts[^1] = last with
        {
            Outcome = TranslationRouteAttemptOutcome.RejectedByQualityGuard,
            FailureReason = "Post-stream quality guard rejected assembled output."
        };
    }

    private static string FormatFailureReason(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message}";

    private enum StreamAttemptStatus
    {
        Succeeded,
        FirstTokenTimeout,
        FailedBeforeFirstToken,
        FailedAfterFirstToken
    }

    private sealed record StreamAttemptResult(
        StreamAttemptStatus Status,
        IReadOnlyList<string> Deltas,
        Exception? Error,
        TimeSpan Duration,
        TimeSpan? FirstTokenLatency);
}
