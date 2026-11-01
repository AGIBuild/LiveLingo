using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Tests.Support;

namespace LiveLingo.Core.Tests.Cleanup;

public class CleanupSchedulerTests
{
    private sealed class FakeCleaner : ITextCleaner
    {
        public sealed record Call(CleanupRequest Request, TaskCompletionSource<string> Result, CancellationToken Token);

        private readonly object _gate = new();
        private readonly List<Call> _calls = [];

        public bool HonorCancellation { get; set; } = true;

        public IReadOnlyList<Call> Calls
        {
            get
            {
                lock (_gate)
                    return _calls.ToArray();
            }
        }

        public Task<string> CleanAsync(CleanupRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
                _calls.Add(new Call(request, tcs, ct));
            if (HonorCancellation)
                ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public static readonly TimeSpan Holdback = TimeSpan.FromSeconds(2);

        public DraftSession Session { get; } = new(hasTarget: true);
        public FakeCleaner Cleaner { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public CleanupScheduler Scheduler { get; }

        public Harness(IReadOnlyList<string>? glossary = null)
        {
            Session.Mutate(draft => draft.Output = OutputKind.Clean);
            Scheduler = new CleanupScheduler(Session, Cleaner, glossary ?? [], Holdback, contextUnits: 2, Time);
        }

        public void Speak(params string[] sentences) =>
            Session.Mutate(draft =>
            {
                foreach (var sentence in sentences)
                    draft.Append(sentence);
            });

        public void Recording(bool on) => Session.Mutate(draft => draft.Recording = on);

        public void Pending(string text) => Session.Mutate(draft => draft.SetPending(text));

        public string? CleanOf(int index) => Session.Read(draft => draft.Units[index].Clean.Text);

        public string ErrorOf(int index) => Session.Read(draft => draft.Units[index].Clean.Error);

        public GateRejection? RejectionOf(int index) => Session.Read(draft => draft.Units[index].Clean.Rejection);

        public async Task<FakeCleaner.Call> NextCall(int count)
        {
            await WaitUntil(() => Cleaner.Calls.Count >= count);
            return Cleaner.Calls[count - 1];
        }

        public ValueTask DisposeAsync() => Scheduler.DisposeAsync();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task ASentence_IsCleanedOnceSomethingFollowsIt_OneAtATimeInOrder()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Speak("呃，先改接口。", "然后补测试。");

        var first = await h.NextCall(1);
        Assert.Equal("呃，先改接口。", first.Request.Target);
        Assert.Equal("", first.Request.Preceding);
        Assert.True(first.Request.Continues);
        await Task.Delay(50);
        Assert.Single(h.Cleaner.Calls);

        first.Result.SetResult("先改接口。");
        h.Time.Advance(Harness.Holdback);
        h.Time.Timer.Fire();
        var second = await h.NextCall(2);
        Assert.Equal("然后补测试。", second.Request.Target);
        Assert.Equal("先改接口。", second.Request.Preceding);
        Assert.False(second.Request.Continues);
    }

    [Fact]
    public async Task TheLastSentence_WaitsForTheHoldback_WhileTheSpeakerMayStillContinue()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Speak("先改接口。");
        await Task.Delay(50);
        Assert.Empty(h.Cleaner.Calls);
        Assert.Equal(Harness.Holdback, h.Time.Timer.Due);

        h.Time.Advance(Harness.Holdback);
        h.Time.Timer.Fire();

        var call = await h.NextCall(1);
        Assert.False(call.Request.Continues);
    }

    [Fact]
    public async Task APendingHypothesis_CountsAsSomethingFollowing()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Speak("先改接口。");
        h.Pending("然后");

        var call = await h.NextCall(1);

        Assert.True(call.Request.Continues);
    }

    [Fact]
    public async Task WhenRecordingHasStopped_TheLastSentenceIsCleanedImmediately()
    {
        await using var h = new Harness();
        h.Speak("先改接口。");

        var call = await h.NextCall(1);

        Assert.False(call.Request.Continues);
    }

    [Fact]
    public async Task StoppingRecording_ReleasesTheHeldBackSentence()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Speak("先改接口。");
        await Task.Delay(50);
        Assert.Empty(h.Cleaner.Calls);

        h.Recording(false);

        await h.NextCall(1);
    }

    [Fact]
    public async Task AFaithfulResult_ReplacesTheCleanText_WithoutTheModelsOuterWhitespace()
    {
        await using var h = new Harness();
        h.Speak("呃，先改接口。");

        (await h.NextCall(1)).Result.SetResult("  先改接口。\n");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Equal("先改接口。", h.CleanOf(0));
        Assert.Null(h.RejectionOf(0));
    }

    [Fact]
    public async Task AResultThatFailsTheFidelityGate_KeepsTheOriginal_AndSaysWhy()
    {
        await using var h = new Harness();
        h.Speak("端口改成 8080。");

        (await h.NextCall(1)).Result.SetResult("端口改一下。");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Equal("端口改成 8080。", h.CleanOf(0));
        Assert.Equal(new GateRejection(GateReason.NumberLost, "8080"), h.RejectionOf(0));
    }

    [Fact]
    public async Task TheGlossary_IsPassedToTheCleanerAndTheGate()
    {
        await using var h = new Harness(glossary: ["CancellationToken"]);
        h.Speak("用 CancellationToken 取消请求。");

        var call = await h.NextCall(1);
        Assert.Equal(["CancellationToken"], call.Request.Glossary);
        call.Result.SetResult("用令牌取消请求。");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Equal("用 CancellationToken 取消请求。", h.CleanOf(0));
        Assert.Equal(GateReason.TermLost, h.RejectionOf(0)?.Reason);
    }

    [Fact]
    public async Task AUserEdit_WinsOverALateCleanupResult()
    {
        await using var h = new Harness();
        h.Speak("先改接口。");
        var call = await h.NextCall(1);

        h.Session.Mutate(draft => draft.Edit(0, UnitKind.Clean, "我改过的。"));
        await WaitUntil(() => call.Token.IsCancellationRequested);
        call.Result.TrySetResult("模型结果。");
        await Task.Delay(50);

        Assert.Equal("我改过的。", h.CleanOf(0));
    }

    [Fact]
    public async Task EditingTheSource_CancelsItsRequest_AndCleansTheNewText()
    {
        await using var h = new Harness();
        h.Speak("旧句子。");
        var stale = await h.NextCall(1);

        h.Session.Mutate(draft => draft.SyncSource([new SourceUnit("新句子。")], DraftRoute.Units));

        await WaitUntil(() => stale.Token.IsCancellationRequested);
        var fresh = await h.NextCall(2);
        Assert.Equal("新句子。", fresh.Request.Target);
    }

    [Fact]
    public async Task NothingIsCleaned_UnlessCleanIsTheSelectedOutput()
    {
        await using var h = new Harness();
        h.Session.Mutate(draft => draft.Output = OutputKind.Translation);

        h.Speak("先改接口。");
        await Task.Delay(50);

        Assert.Empty(h.Cleaner.Calls);
    }

    [Fact]
    public async Task SwitchingToClean_CleansWhatWasSpokenMeanwhile()
    {
        await using var h = new Harness();
        h.Session.Mutate(draft => draft.Output = OutputKind.Raw);
        h.Speak("先改接口。");
        await Task.Delay(50);
        Assert.Empty(h.Cleaner.Calls);

        h.Session.Mutate(draft => draft.Output = OutputKind.Clean);

        await h.NextCall(1);
    }

    [Fact]
    public async Task AServiceFailure_FailsEveryPendingSentenceTogether()
    {
        await using var h = new Harness();
        h.Speak("一。", "二。", "三。");

        (await h.NextCall(1)).Result.SetException(new HttpRequestException("offline"));

        await WaitUntil(() => h.ErrorOf(2).Length > 0);
        await Task.Delay(50);
        Assert.Single(h.Cleaner.Calls);
        Assert.Equal("offline", h.ErrorOf(0));
        Assert.Equal("offline", h.ErrorOf(1));
    }

    [Fact]
    public async Task ASentenceSpecificFailure_DoesNotFailTheOthers()
    {
        await using var h = new Harness();
        h.Speak("一。", "二。");

        (await h.NextCall(1)).Result.SetException(new InvalidDataException("empty output"));
        var second = await h.NextCall(2);

        Assert.Equal("empty output", h.ErrorOf(0));
        Assert.Equal("", h.ErrorOf(1));
        second.Result.SetResult("二。");
    }

    [Fact]
    public async Task RetryingAFailedSentence_CleansItAgain()
    {
        await using var h = new Harness();
        h.Speak("一。");
        (await h.NextCall(1)).Result.SetException(new InvalidDataException("bad"));
        await WaitUntil(() => h.ErrorOf(0).Length > 0);

        h.Session.Mutate(draft => draft.Retry(0, UnitKind.Clean));

        await h.NextCall(2);
    }

    [Fact]
    public async Task OnlyTheLastContextUnits_AreGivenAsPreceding()
    {
        await using var h = new Harness();
        h.Speak("一。", "二。", "三。", "四。");

        for (var i = 1; i <= 4; i++)
        {
            var call = await h.NextCall(i);
            call.Result.SetResult(call.Request.Target);
        }

        Assert.Equal("二。三。", h.Cleaner.Calls[3].Request.Preceding);
    }

    [Fact]
    public async Task Dispose_CancelsTheRequestInFlight_AndStopsTheTimer()
    {
        var h = new Harness();
        h.Recording(true);
        h.Speak("一。", "二。");
        var call = await h.NextCall(1);

        await h.DisposeAsync();

        Assert.True(call.Token.IsCancellationRequested);
        Assert.True(h.Time.Timer.Disposed);
        h.Speak("三。");
        await Task.Delay(50);
        Assert.Single(h.Cleaner.Calls);
    }
}
