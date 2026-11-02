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
        public static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

        public DraftSession Session { get; } = new(hasTarget: true);
        public FakeCleaner Cleaner { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public CleanupScheduler Scheduler { get; }

        public Harness(IReadOnlyList<string>? glossary = null)
        {
            Session.Mutate(draft => draft.Output = OutputKind.Clean);
            Scheduler = new CleanupScheduler(Session, Cleaner, glossary ?? [], contextChars: 4, Time);
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
    public async Task Units_AreCleanedOneAtATimeInOrder()
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
        var second = await h.NextCall(2);
        Assert.Equal("然后补测试。", second.Request.Target);
        Assert.Equal("先改接口。", second.Request.Preceding);
        Assert.True(second.Request.Continues); // still recording
    }

    [Fact]
    public async Task TheLastUnit_IsCleanedAtOnce_AndTheSpeakerMayStillContinueWhileRecording()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Speak("先改接口。");

        var call = await h.NextCall(1);

        Assert.True(call.Request.Continues);
    }

    [Fact]
    public async Task WhenRecordingHasStopped_TheLastUnitIsTheEndOfTheSpeech()
    {
        await using var h = new Harness();
        h.Speak("先改接口。");

        var call = await h.NextCall(1);

        Assert.False(call.Request.Continues);
    }

    [Fact]
    public async Task APendingHypothesis_CountsAsSomethingFollowing()
    {
        await using var h = new Harness();
        h.Recording(true);
        h.Pending("然后");
        h.Speak("先改接口。");

        var call = await h.NextCall(1);

        Assert.True(call.Request.Continues);
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
    public async Task ARefusedPassage_IsCleanedSentenceBySentence_SoOneBadSentenceDoesNotCostTheRest()
    {
        await using var h = new Harness();
        h.Speak("呃，先改接口。然后补测试。");

        (await h.NextCall(1)).Result.SetResult("");
        var first = await h.NextCall(2);
        Assert.Equal("呃，先改接口。", first.Request.Target);
        Assert.Equal("", first.Request.Preceding);
        Assert.True(first.Request.Continues);
        first.Result.SetResult("先改接口。");
        var second = await h.NextCall(3);
        Assert.Equal("然后补测试。", second.Request.Target);
        Assert.Equal("先改接口。", second.Request.Preceding);
        Assert.False(second.Request.Continues);
        second.Result.SetResult("补一个新测试，并且重构整个下载模块。");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Equal("先改接口。然后补测试。", h.CleanOf(0));
        Assert.Null(h.RejectionOf(0));
    }

    [Fact]
    public async Task ARefusedPassage_KeepsTheOriginal_WhenNoSentenceOfItPasses()
    {
        await using var h = new Harness();
        h.Speak("端口改成 8080。超时设成三十秒。");

        (await h.NextCall(1)).Result.SetResult("端口改一下。超时改一下。");
        (await h.NextCall(2)).Result.SetResult("端口改一下。");
        (await h.NextCall(3)).Result.SetResult("");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Equal("端口改成 8080。超时设成三十秒。", h.CleanOf(0));
        Assert.Equal(GateReason.NumberLost, h.RejectionOf(0)?.Reason);
    }

    [Fact]
    public async Task ARefusedMultiLineText_IsNotSplitIntoSentences()
    {
        await using var h = new Harness();
        h.Session.Mutate(draft => draft.SyncSource([new SourceUnit("第一行，端口 8080。\n第二行。")], DraftRoute.Whole));
        await Task.Delay(50);
        h.Time.Advance(Harness.TypingPause);
        h.Time.Timer.Fire();

        (await h.NextCall(1)).Result.SetResult("全删了。");

        await WaitUntil(() => h.CleanOf(0) is not null);
        Assert.Single(h.Cleaner.Calls);
        Assert.NotNull(h.RejectionOf(0));
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
        h.Time.Advance(Harness.TypingPause);
        h.Time.Timer.Fire();
        var fresh = await h.NextCall(2);
        Assert.Equal("新句子。", fresh.Request.Target);
    }

    [Fact]
    public async Task TypedText_IsCleanedAfterAPauseInTyping_NotOncePerKey()
    {
        await using var h = new Harness();

        foreach (var typed in new[] { "你", "你好", "你好吗" })
            h.Session.Mutate(draft => draft.SyncSource([new SourceUnit(typed)], DraftRoute.Units));
        await WaitUntil(() => h.Time.Timer.Due == Harness.TypingPause);
        Assert.Empty(h.Cleaner.Calls);

        h.Time.Advance(Harness.TypingPause);
        h.Time.Timer.Fire();
        var call = await h.NextCall(1);
        Assert.Equal("你好吗", call.Request.Target);
        Assert.Single(h.Cleaner.Calls);
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

        (await h.NextCall(1)).Result.SetException(new InvalidOperationException("empty output"));
        var second = await h.NextCall(2);

        Assert.Equal("empty output", h.ErrorOf(0));
        Assert.Equal("", h.ErrorOf(1));
        second.Result.SetResult("二。");
    }

    [Fact]
    public async Task ATruncatedAnswer_KeepsTheOriginal_InsteadOfFailingTheUnit()
    {
        await using var h = new Harness();
        h.Speak("一。", "二。");

        (await h.NextCall(1)).Result.SetException(new InvalidDataException("Incomplete completion stream: finish=length"));
        var second = await h.NextCall(2);
        second.Result.SetResult("二。");

        await WaitUntil(() => h.CleanOf(1) is not null);
        Assert.Equal("一。", h.CleanOf(0));
        Assert.Equal(new GateRejection(GateReason.Incomplete), h.RejectionOf(0));
        Assert.Equal("", h.ErrorOf(0));
        Assert.True(h.Session.Read(d => d.ContentReadiness().IsReady));
    }

    [Fact]
    public async Task RetryingAFailedSentence_CleansItAgain()
    {
        await using var h = new Harness();
        h.Speak("一。");
        (await h.NextCall(1)).Result.SetException(new InvalidOperationException("bad"));
        await WaitUntil(() => h.ErrorOf(0).Length > 0);

        h.Session.Mutate(draft => draft.Retry(0, UnitKind.Clean));

        await h.NextCall(2);
    }

    [Fact]
    public async Task Preceding_ReachesBackAsFarAsTheCharacterBudget_WithWholeUnits()
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
    public async Task Preceding_AlwaysIncludesTheNearestUnit_EvenWhenItExceedsTheBudget()
    {
        await using var h = new Harness();
        h.Speak("这是一个比预算长得多的上一段。", "下一段。");

        var first = await h.NextCall(1);
        first.Result.SetResult(first.Request.Target);
        var second = await h.NextCall(2);

        Assert.Equal("这是一个比预算长得多的上一段。", second.Request.Preceding);
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
