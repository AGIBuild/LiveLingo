using LiveLingo.Core.Models;
using LiveLingo.Core.Tests.Support;

namespace LiveLingo.Core.Tests.Models;

public class ResidentModelTests
{
    private sealed class Loaded(string key) : IDisposable
    {
        public string Key { get; } = key;
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

        public ManualTimeProvider Time { get; } = new();
        public List<Loaded> Loads { get; } = [];
        public List<ModelResidency> States { get; } = [];
        public TaskCompletionSource? Gate { get; set; }
        public ResidentModel<Loaded> Model { get; }

        public Harness()
        {
            Model = new ResidentModel<Loaded>("test model", key =>
            {
                Gate?.Task.Wait();
                var loaded = new Loaded(key);
                lock (Loads)
                    Loads.Add(loaded);
                return loaded;
            }, Idle, Time);
            Model.StateChanged += States.Add;
        }

        public ValueTask DisposeAsync() => Model.DisposeAsync();
    }

    [Fact]
    public async Task Warm_LoadsOnce_AndTheNextUseDoesNotLoadAgain()
    {
        await using var h = new Harness();

        await h.Model.WarmAsync("a");
        using var lease = await h.Model.AcquireAsync("a");

        Assert.Single(h.Loads);
        Assert.Same(h.Loads[0], lease.Value);
        Assert.Equal([ModelResidency.Loading, ModelResidency.Ready], h.States);
        Assert.Equal(ModelResidency.Ready, h.Model.State);
    }

    [Fact]
    public async Task CallersAskingAtTheSameTime_ShareOneLoad()
    {
        await using var h = new Harness();
        h.Gate = new TaskCompletionSource();

        var warm = h.Model.WarmAsync("a");
        var acquire = h.Model.AcquireAsync("a");
        Assert.Equal(ModelResidency.Loading, h.Model.State);
        h.Gate.SetResult();
        await warm;
        using var lease = await acquire;

        Assert.Single(h.Loads);
    }

    [Fact]
    public async Task AFailedLoad_LeavesTheModelUnloaded_AndTheNextAskTriesAgain()
    {
        var attempts = 0;
        await using var model = new ResidentModel<Loaded>("flaky", key => ++attempts == 1 ? throw new IOException("disk") : new Loaded(key), Harness.Idle, new ManualTimeProvider());

        await Assert.ThrowsAsync<IOException>(() => model.WarmAsync("a"));
        Assert.Equal(ModelResidency.Unloaded, model.State);

        await model.WarmAsync("a");
        Assert.Equal(ModelResidency.Ready, model.State);
    }

    [Fact]
    public async Task AnotherKey_ReloadsTheModel_AndUnloadsTheOldOne()
    {
        await using var h = new Harness();
        await h.Model.WarmAsync("a");

        await h.Model.WarmAsync("b");

        Assert.Equal(["a", "b"], h.Loads.Select(l => l.Key));
        Assert.True(h.Loads[0].Disposed);
        Assert.False(h.Loads[1].Disposed);
    }

    [Fact]
    public async Task AnotherKey_WhileTheModelIsLent_IsAnError()
    {
        await using var h = new Harness();
        using var lease = await h.Model.AcquireAsync("a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Model.WarmAsync("b"));

        Assert.False(h.Loads[0].Disposed);
    }

    [Fact]
    public async Task WhenIdleForTheTimeout_TheModelIsUnloaded_AndLoadedAgainWhenAsked()
    {
        await using var h = new Harness();
        await h.Model.WarmAsync("a");
        Assert.Equal(Harness.Idle, h.Time.Timer.Due);

        h.Time.Timer.Fire();

        Assert.True(h.Loads[0].Disposed);
        Assert.Equal(ModelResidency.Unloaded, h.Model.State);

        using var lease = await h.Model.AcquireAsync("a");
        Assert.Equal(2, h.Loads.Count);
        Assert.Equal(ModelResidency.Ready, h.Model.State);
    }

    [Fact]
    public async Task AModelThatIsLent_IsNeverUnloadedForIdleness_AndCountsIdleFromTheLastRelease()
    {
        await using var h = new Harness();
        var lease = await h.Model.AcquireAsync("a");
        Assert.Equal(Timeout.InfiniteTimeSpan, h.Time.Timer.Due);

        h.Time.Timer.Fire();
        Assert.False(h.Loads[0].Disposed);

        lease.Dispose();
        Assert.Equal(Harness.Idle, h.Time.Timer.Due);
        h.Time.Timer.Fire();
        Assert.True(h.Loads[0].Disposed);
    }

    [Fact]
    public async Task DisposingALeaseTwice_GivesTheModelBackOnce()
    {
        await using var h = new Harness();
        var first = await h.Model.AcquireAsync("a");
        using var second = await h.Model.AcquireAsync("a");

        first.Dispose();
        first.Dispose();
        h.Time.Timer.Fire();

        Assert.False(h.Loads[0].Disposed);
    }

    [Fact]
    public async Task Dispose_UnloadsTheModel_ButOnlyAfterTheLastLeaseWasGivenBack()
    {
        var h = new Harness();
        var lease = await h.Model.AcquireAsync("a");

        await h.Model.DisposeAsync();
        Assert.False(h.Loads[0].Disposed);

        lease.Dispose();
        Assert.True(h.Loads[0].Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Model.WarmAsync("a"));
    }

    [Fact]
    public async Task Dispose_UnloadsAnIdleModelAtOnce()
    {
        var h = new Harness();
        await h.Model.WarmAsync("a");

        await h.Model.DisposeAsync();

        Assert.True(h.Loads[0].Disposed);
    }
}
