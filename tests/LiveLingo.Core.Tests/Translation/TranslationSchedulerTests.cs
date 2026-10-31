using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class TranslationSchedulerTests
{
    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan Due { get; private set; } = Timeout.InfiniteTimeSpan;
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimer Timer { get; private set; } = null!;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Timer = new ManualTimer(callback, state);
    }

    private sealed class FakeTranslator : ITranslator
    {
        public sealed record Call(
            TranslationRequest Request,
            TaskCompletionSource<TranslatedText> Result,
            CancellationToken Token);

        private readonly object _gate = new();
        private readonly List<Call> _calls = [];

        /// <summary>When false the fake keeps running after cancellation, like a server that finishes anyway.</summary>
        public bool HonorCancellation { get; set; } = true;

        public IReadOnlyList<Call> Calls
        {
            get
            {
                lock (_gate)
                    return _calls.ToArray();
            }
        }

        public Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<TranslatedText>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
                _calls.Add(new Call(request, tcs, ct));
            if (HonorCancellation)
                ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public DraftSession Session { get; } = new(hasTarget: true);
        public FakeTranslator Translator { get; } = new();
        public ManualTimeProvider Time { get; } = new();
        public TranslationScheduler Scheduler { get; }

        public Harness(TargetLanguage target = TargetLanguage.English) =>
            Scheduler = new TranslationScheduler(Session, Translator, target, TimeSpan.FromSeconds(1.5), Time);

        public void Source(params SourceUnit[] units) =>
            Session.Mutate(draft => draft.SyncSource(units, DraftRoute.Units));

        public void Output(OutputKind output) => Session.Mutate(draft => draft.Output = output);

        public string? TranslationOf(int index) => Session.Read(draft => draft.Units[index].Translation.Text);

        public string ErrorOf(int index) => Session.Read(draft => draft.Units[index].Translation.Error);

        public async Task<FakeTranslator.Call> NextCall(int count)
        {
            await WaitUntil(() => Translator.Calls.Count >= count);
            return Translator.Calls[count - 1];
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

    private static TranslatedText Done(string text) => new(text, 1, 2);

    [Fact]
    public async Task CompleteUnits_AreTranslatedInOrder_OneRequestAtATime()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"), new SourceUnit("再更新索引。"));
        h.Output(OutputKind.Translation);

        var first = await h.NextCall(1);
        Assert.Equal("先检查缓存。", first.Request.Source);
        Assert.Equal(TargetLanguage.English, first.Request.Target);
        await Task.Delay(50);
        Assert.Single(h.Translator.Calls);

        first.Result.SetResult(Done("Check the cache first."));
        var second = await h.NextCall(2);
        Assert.Equal("再更新索引。", second.Request.Source);
        second.Result.SetResult(Done("Then update the index."));

        await WaitUntil(() => h.TranslationOf(1) is not null);
        Assert.Equal("Check the cache first.", h.TranslationOf(0));
        Assert.Equal("Then update the index.", h.TranslationOf(1));
    }

    [Fact]
    public async Task NothingIsTranslated_UnlessTranslationIsTheSelectedOutput()
    {
        await using var h = new Harness();

        h.Source(new SourceUnit("先检查缓存。"));
        await Task.Delay(50);

        Assert.Empty(h.Translator.Calls);
    }

    [Fact]
    public async Task UserEditedTranslations_AreNeverRequested()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Session.Mutate(draft => draft.Edit(0, UnitKind.Translation, "Check the cache."));

        h.Output(OutputKind.Translation);
        await Task.Delay(50);

        Assert.Empty(h.Translator.Calls);
    }

    [Fact]
    public async Task UnfinishedTail_WaitsForTheIdleTimer()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("然后更新", IsComplete: false));
        h.Output(OutputKind.Translation);
        await Task.Delay(50);
        Assert.Empty(h.Translator.Calls);
        Assert.Equal(TimeSpan.FromSeconds(1.5), h.Time.Timer.Due);

        h.Time.Timer.Fire();

        var call = await h.NextCall(1);
        Assert.Equal("然后更新", call.Request.Source);
    }

    [Fact]
    public async Task ChangingTheSource_RearmsTheIdleTimer_AndCancelsTheFlush()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("然后更新", IsComplete: false));
        h.Output(OutputKind.Translation);
        h.Scheduler.FlushTail();
        var call = await h.NextCall(1);
        call.Result.SetResult(Done("Then update"));
        await WaitUntil(() => h.TranslationOf(0) is not null);

        h.Source(new SourceUnit("然后更新", IsComplete: false), new SourceUnit("再来一个", IsComplete: false));
        await Task.Delay(50);

        Assert.Single(h.Translator.Calls);
        Assert.Equal(TimeSpan.FromSeconds(1.5), h.Time.Timer.Due);
    }

    [Fact]
    public async Task FlushTail_TranslatesTheUnfinishedUnitImmediately()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("然后更新", IsComplete: false));
        h.Output(OutputKind.Translation);

        h.Scheduler.FlushTail();

        Assert.Equal("然后更新", (await h.NextCall(1)).Request.Source);
    }

    [Fact]
    public async Task EditingTheSourceUnit_CancelsItsRequest_AndTranslatesTheNewText()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("旧句子。"));
        h.Output(OutputKind.Translation);
        var stale = await h.NextCall(1);

        h.Source(new SourceUnit("新句子。"));

        await WaitUntil(() => stale.Token.IsCancellationRequested);
        var fresh = await h.NextCall(2);
        Assert.Equal("新句子。", fresh.Request.Source);
        fresh.Result.SetResult(Done("New sentence."));
        await WaitUntil(() => h.TranslationOf(0) is not null);
        Assert.Equal("New sentence.", h.TranslationOf(0));
    }

    [Fact]
    public async Task LateResultOfAStaleRequest_IsDiscarded()
    {
        await using var h = new Harness();
        h.Translator.HonorCancellation = false;
        h.Source(new SourceUnit("旧句子。"));
        h.Output(OutputKind.Translation);
        var stale = await h.NextCall(1);
        h.Source(new SourceUnit("新句子。"));
        await WaitUntil(() => stale.Token.IsCancellationRequested);

        stale.Result.SetResult(Done("Old sentence."));

        var fresh = await h.NextCall(2);
        Assert.Null(h.TranslationOf(0));
        fresh.Result.SetResult(Done("New sentence."));
        await WaitUntil(() => h.TranslationOf(0) is not null);
        Assert.Equal("New sentence.", h.TranslationOf(0));
    }

    [Fact]
    public async Task SetTarget_DropsTranslations_CancelsInFlight_AndUsesTheNewLanguage()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Output(OutputKind.Translation);
        var english = await h.NextCall(1);
        english.Result.SetResult(Done("Check the cache first."));
        await WaitUntil(() => h.TranslationOf(0) is not null);

        h.Scheduler.SetTarget(TargetLanguage.Chinese);

        var chinese = await h.NextCall(2);
        Assert.Equal(TargetLanguage.Chinese, chinese.Request.Target);
        Assert.Equal(TargetLanguage.Chinese, h.Scheduler.Target);
        Assert.Null(h.TranslationOf(0));
    }

    [Fact]
    public async Task SetTarget_ToTheSameLanguage_KeepsTranslations()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Output(OutputKind.Translation);
        (await h.NextCall(1)).Result.SetResult(Done("Check the cache first."));
        await WaitUntil(() => h.TranslationOf(0) is not null);

        h.Scheduler.SetTarget(TargetLanguage.English);
        await Task.Delay(50);

        Assert.Equal("Check the cache first.", h.TranslationOf(0));
        Assert.Single(h.Translator.Calls);
    }

    [Fact]
    public async Task AFailedUnit_IsMarkedWithItsReason_AndTheNextUnitStillRuns()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"), new SourceUnit("再更新索引。"));
        h.Output(OutputKind.Translation);

        (await h.NextCall(1)).Result.SetException(new HttpRequestException("连接被拒绝"));
        var second = await h.NextCall(2);

        Assert.Equal("再更新索引。", second.Request.Source);
        Assert.Equal("连接被拒绝", h.ErrorOf(0));
        Assert.Null(h.TranslationOf(0));
    }

    [Fact]
    public async Task RetryingAFailedUnit_TranslatesItAgain()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Output(OutputKind.Translation);
        (await h.NextCall(1)).Result.SetException(new InvalidDataException("bad"));
        await WaitUntil(() => h.ErrorOf(0).Length > 0);

        h.Session.Mutate(draft => draft.Retry(0, UnitKind.Translation));

        var again = await h.NextCall(2);
        Assert.Equal("先检查缓存。", again.Request.Source);
    }

    [Fact]
    public async Task AfterCommit_NothingIsTranslated()
    {
        await using var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Output(OutputKind.Raw);
        h.Session.Mutate(draft => draft.RequestCommit());
        h.Output(OutputKind.Translation);
        await Task.Delay(50);

        Assert.Empty(h.Translator.Calls);
    }

    [Fact]
    public async Task Dispose_CancelsTheRequestInFlight_AndStopsTheTimer()
    {
        var h = new Harness();
        h.Source(new SourceUnit("先检查缓存。"));
        h.Output(OutputKind.Translation);
        var call = await h.NextCall(1);

        await h.DisposeAsync();

        Assert.True(call.Token.IsCancellationRequested);
        Assert.True(h.Time.Timer.Disposed);
        h.Source(new SourceUnit("之后的修改。"));
        await Task.Delay(50);
        Assert.Single(h.Translator.Calls);
    }
}
