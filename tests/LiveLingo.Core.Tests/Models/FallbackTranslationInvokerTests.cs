using System.Runtime.CompilerServices;
using LiveLingo.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LiveLingo.Core.Tests.Models;

public sealed class FallbackTranslationInvokerTests
{
    private static readonly ModelProfile LocalProfile = new(
        "local-model", "Local", ModelTaskType.Translation,
        ModelProviderKind.LlamaServer, ModelRuntimeKind.LlamaServer,
        ModelExecutionKind.ChatCompletions, [],
        new ModelDescriptor("local-model", "Local", "", 0, ModelType.Translation),
        SupportsAllLanguages: true);

    private static readonly ModelProfile CloudProfile = new(
        "cloud-model", "Cloud", ModelTaskType.Translation,
        ModelProviderKind.OpenAICompatible, ModelRuntimeKind.RemoteHttp,
        ModelExecutionKind.ChatCompletions, [],
        new ModelDescriptor("cloud-model", "Cloud", "", 0, ModelType.Translation),
        SupportsAllLanguages: true);

    private static readonly TranslationRoutePlan TwoCandidatePlan = new(
    [
        new TranslationRouteCandidate(LocalProfile, TranslationRouteTier.Local, TimeSpan.FromSeconds(5)),
        new TranslationRouteCandidate(CloudProfile, TranslationRouteTier.Cloud, TimeSpan.FromSeconds(3))
    ]);

    private static ModelInvocationRequest BuildRequest(TranslationRouteCandidate candidate) =>
        new(candidate.Profile, ModelTaskType.Translation,
            [new ModelChatMessage("user", "hi")],
            new ModelInvocationOptions(256, 0.3f, 0.9f, [], false));

    private static (FallbackTranslationInvoker invoker, InProcessTranslationTelemetry telemetry)
        CreateInvoker(IModelInvocationService service)
    {
        var telemetry = new InProcessTranslationTelemetry();
        return (new FallbackTranslationInvoker(service, telemetry), telemetry);
    }

    private static TranslationRouteTrace SingleTrace(InProcessTranslationTelemetry telemetry)
    {
        var traces = telemetry.RecentTraces;
        Assert.Single(traces);
        return traces[0];
    }

    // ── Non-streaming ────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_ReturnsPrimary_WhenPrimarySucceeds()
    {
        var service = Substitute.For<IModelInvocationService>();
        service.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("local result"));
        var (invoker, telemetry) = CreateInvoker(service);

        var outcome = await invoker.InvokeAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null);

        Assert.Equal("local result", outcome.Text);
        Assert.Equal(LocalProfile.Id, outcome.Candidate.Profile.Id);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteInvocationKind.NonStreaming, trace.Kind);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.False(trace.UsedFallback);
        var attempt = Assert.Single(trace.Attempts);
        Assert.Equal(LocalProfile.Id, attempt.Candidate.Profile.Id);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.Null(attempt.FailureReason);
    }

    [Fact]
    public async Task InvokeAsync_FallsBackToNextCandidate_WhenPrimaryThrows()
    {
        var service = Substitute.For<IModelInvocationService>();
        service.InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.Id == LocalProfile.Id),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("local down"));
        service.InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.Id == CloudProfile.Id),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("cloud result"));
        var (invoker, telemetry) = CreateInvoker(service);

        var outcome = await invoker.InvokeAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null);

        Assert.Equal("cloud result", outcome.Text);
        Assert.Equal(CloudProfile.Id, outcome.Candidate.Profile.Id);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.True(trace.UsedFallback);
        Assert.Equal(2, trace.Attempts.Count);
        Assert.Equal(TranslationRouteAttemptOutcome.FailedWithException, trace.Attempts[0].Outcome);
        Assert.Contains("local down", trace.Attempts[0].FailureReason);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, trace.Attempts[1].Outcome);
        Assert.Equal(CloudProfile.Id, trace.WinningCandidate?.Profile.Id);
    }

    [Fact]
    public async Task InvokeAsync_FallsBackWhenQualityGuardRejects()
    {
        var service = Substitute.For<IModelInvocationService>();
        service.InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.Id == LocalProfile.Id),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("ok"));
        service.InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.Id == CloudProfile.Id),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("A much better cloud translation."));
        var (invoker, telemetry) = CreateInvoker(service);

        TranslationQualityAssertion guard = t => t.Length > 5;

        var outcome = await invoker.InvokeAsync(TwoCandidatePlan, BuildRequest, guard);

        Assert.Equal(CloudProfile.Id, outcome.Candidate.Profile.Id);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.True(trace.UsedFallback);
        Assert.Equal(TranslationRouteAttemptOutcome.RejectedByQualityGuard, trace.Attempts[0].Outcome);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, trace.Attempts[1].Outcome);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsWhenAllCandidatesFail()
    {
        var service = Substitute.For<IModelInvocationService>();
        service.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("all down"));
        var (invoker, telemetry) = CreateInvoker(service);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null));

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.AllCandidatesFailed, trace.Outcome);
        Assert.Null(trace.WinningCandidate);
        Assert.Equal(2, trace.Attempts.Count);
        Assert.All(trace.Attempts, a =>
            Assert.Equal(TranslationRouteAttemptOutcome.FailedWithException, a.Outcome));
    }

    [Fact]
    public async Task InvokeAsync_PropagatesUserCancellation()
    {
        var service = Substitute.For<IModelInvocationService>();
        service.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var (invoker, telemetry) = CreateInvoker(service);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => invoker.InvokeAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null, cts.Token));

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Cancelled, trace.Outcome);
        Assert.Null(trace.WinningCandidate);
    }

    // ── Streaming ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeStreamingAsync_YieldsPrimaryDeltas_WhenPrimarySucceeds()
    {
        var service = new StubInvocationService
        {
            StreamBehaviour = (_, _) => StreamingSequence("Hello", " world")
        };
        var (invoker, telemetry) = CreateInvoker(service);

        var collected = new List<string>();
        await foreach (var update in invoker.InvokeStreamingAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null))
        {
            collected.Add(update.Delta);
            Assert.Equal(LocalProfile.Id, update.Candidate.Profile.Id);
        }

        Assert.Equal(["Hello", " world"], collected);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteInvocationKind.Streaming, trace.Kind);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.False(trace.UsedFallback);
        var attempt = Assert.Single(trace.Attempts);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, attempt.Outcome);
        Assert.NotNull(attempt.FirstTokenLatency);
    }

    [Fact]
    public async Task InvokeStreamingAsync_FallsBackBeforeFirstToken_WhenPrimaryThrows()
    {
        var service = new StubInvocationService
        {
            StreamBehaviour = (req, _) => req.Profile.Id == LocalProfile.Id
                ? ThrowBeforeFirstToken(new InvalidOperationException("local crashed"))
                : StreamingSequence("cloud answer")
        };
        var (invoker, telemetry) = CreateInvoker(service);

        var collected = new List<(string id, string delta)>();
        await foreach (var update in invoker.InvokeStreamingAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null))
            collected.Add((update.Candidate.Profile.Id, update.Delta));

        Assert.Single(collected);
        Assert.Equal((CloudProfile.Id, "cloud answer"), collected[0]);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.True(trace.UsedFallback);
        Assert.Equal(TranslationRouteAttemptOutcome.FailedWithException, trace.Attempts[0].Outcome);
        Assert.Contains("local crashed", trace.Attempts[0].FailureReason);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, trace.Attempts[1].Outcome);
    }

    [Fact]
    public async Task InvokeStreamingAsync_FallsBack_WhenFirstTokenBudgetExpires()
    {
        var shortBudgetPlan = new TranslationRoutePlan(
        [
            new TranslationRouteCandidate(LocalProfile, TranslationRouteTier.Local, TimeSpan.FromMilliseconds(50)),
            new TranslationRouteCandidate(CloudProfile, TranslationRouteTier.Cloud, TimeSpan.FromSeconds(3))
        ]);
        var service = new StubInvocationService
        {
            StreamBehaviour = (req, ct) => req.Profile.Id == LocalProfile.Id
                ? DelayBeforeFirstToken(TimeSpan.FromSeconds(5), ct)
                : StreamingSequence("fast cloud")
        };
        var (invoker, telemetry) = CreateInvoker(service);

        var collected = new List<(string id, string delta)>();
        await foreach (var update in invoker.InvokeStreamingAsync(shortBudgetPlan, BuildRequest, qualityGuard: null))
            collected.Add((update.Candidate.Profile.Id, update.Delta));

        Assert.Single(collected);
        Assert.Equal(CloudProfile.Id, collected[0].id);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.Equal(TranslationRouteAttemptOutcome.FirstTokenBudgetExpired, trace.Attempts[0].Outcome);
        Assert.Contains("50 ms", trace.Attempts[0].FailureReason);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, trace.Attempts[1].Outcome);
    }

    [Fact]
    public async Task InvokeStreamingAsync_ThrowsWhenFailureHappensAfterFirstToken()
    {
        var service = new StubInvocationService
        {
            StreamBehaviour = (_, _) => EmitThenThrow("partial", new InvalidOperationException("mid-stream"))
        };
        var (invoker, telemetry) = CreateInvoker(service);

        var collected = new List<string>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var update in invoker.InvokeStreamingAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null))
                collected.Add(update.Delta);
        });

        Assert.Contains("after partial output", ex.Message, StringComparison.Ordinal);
        Assert.Contains("partial", collected);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.FailedAfterFirstToken, trace.Outcome);
        var attempt = Assert.Single(trace.Attempts);
        Assert.Equal(TranslationRouteAttemptOutcome.FailedAfterFirstToken, attempt.Outcome);
        Assert.NotNull(attempt.FirstTokenLatency);
    }

    [Fact]
    public async Task InvokeStreamingAsync_EmitsReplaceAllUpdate_WhenQualityGuardFailsAndReplacementSucceeds()
    {
        var service = new StubInvocationService
        {
            StreamBehaviour = (_, _) => StreamingSequence("ok"),
            InvokeBehaviour = _ => new ModelInvocationResult("A proper cloud replacement translation.")
        };
        var (invoker, telemetry) = CreateInvoker(service);

        TranslationQualityAssertion guard = t => t.Trim().Length >= 10;

        var updates = new List<TranslationStreamingUpdate>();
        await foreach (var u in invoker.InvokeStreamingAsync(TwoCandidatePlan, BuildRequest, guard))
            updates.Add(u);

        Assert.Equal(2, updates.Count);
        Assert.False(updates[0].ReplaceAll);
        Assert.Equal("ok", updates[0].Delta);
        Assert.True(updates[1].ReplaceAll);
        Assert.Equal("A proper cloud replacement translation.", updates[1].Delta);
        Assert.Equal(CloudProfile.Id, updates[1].Candidate.Profile.Id);

        var trace = SingleTrace(telemetry);
        Assert.Equal(TranslationRouteTraceOutcome.Succeeded, trace.Outcome);
        Assert.True(trace.UsedFallback);
        Assert.Equal(2, trace.Attempts.Count);
        // Streaming winner was retroactively rejected by the post-stream quality guard.
        Assert.Equal(TranslationRouteAttemptOutcome.RejectedByQualityGuard, trace.Attempts[0].Outcome);
        Assert.Equal(TranslationRouteAttemptOutcome.Succeeded, trace.Attempts[1].Outcome);
        Assert.Equal(CloudProfile.Id, trace.WinningCandidate?.Profile.Id);
    }

    [Fact]
    public async Task InvokeStreamingAsync_EmitsDeltasInRealTime_NotBuffered()
    {
        // Proves the invoker yields each delta as it arrives from the producer
        // rather than buffering the entire candidate stream before yielding. The
        // producer blocks on releaseSecond between the two deltas; if the invoker
        // waited for the stream to finish before yielding anything, the caller's
        // first MoveNextAsync would never complete (deadlock) because we only
        // release the producer after observing the first delta.
        var releaseSecond = new TaskCompletionSource();

        var service = new StubInvocationService
        {
            StreamBehaviour = (_, ct) => BlockingPairStream("first", "second", releaseSecond, ct)
        };
        var (invoker, _) = CreateInvoker(service);

        await using var e = invoker.InvokeStreamingAsync(TwoCandidatePlan, BuildRequest, qualityGuard: null)
            .GetAsyncEnumerator();

        var firstMove = e.MoveNextAsync().AsTask();
        Assert.True(await firstMove.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("first", e.Current.Delta);

        // Producer is still parked inside releaseSecond.Task — if the invoker were
        // buffering, it would be parked too and we'd never have reached this line.
        releaseSecond.SetResult();

        Assert.True(await e.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("second", e.Current.Delta);
        Assert.False(await e.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    // --- Streaming helpers ---------------------------------------------------

    private static async IAsyncEnumerable<string> BlockingPairStream(
        string first,
        string second,
        TaskCompletionSource releaseSecond,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return first;
        await releaseSecond.Task.WaitAsync(ct).ConfigureAwait(false);
        yield return second;
    }


    private static async IAsyncEnumerable<string> StreamingSequence(params string[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<string> ThrowBeforeFirstToken(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162 // Unreachable
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<string> DelayBeforeFirstToken(
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(delay, ct).ConfigureAwait(false);
        yield return "late";
    }

    private static async IAsyncEnumerable<string> EmitThenThrow(string first, Exception ex)
    {
        await Task.Yield();
        yield return first;
        await Task.Yield();
        throw ex;
    }

    private sealed class StubInvocationService : IModelInvocationService
    {
        public Func<ModelInvocationRequest, CancellationToken, IAsyncEnumerable<string>>? StreamBehaviour { get; set; }
        public Func<ModelInvocationRequest, ModelInvocationResult>? InvokeBehaviour { get; set; }

        public Task<ModelInvocationResult> InvokeAsync(ModelInvocationRequest request, CancellationToken ct = default)
            => Task.FromResult(InvokeBehaviour?.Invoke(request)
                               ?? throw new InvalidOperationException("InvokeBehaviour not configured."));

        public IAsyncEnumerable<string> InvokeStreamingAsync(ModelInvocationRequest request, CancellationToken ct = default)
            => StreamBehaviour is null
                ? throw new InvalidOperationException("StreamBehaviour not configured.")
                : StreamBehaviour(request, ct);
    }
}
