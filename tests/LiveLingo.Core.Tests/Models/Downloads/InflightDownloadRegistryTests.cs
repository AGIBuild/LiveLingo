using LiveLingo.Core.Models.Downloads;

namespace LiveLingo.Core.Tests.Models.Downloads;

public sealed class InflightDownloadRegistryTests
{
    [Fact]
    public void GetOrAdd_ReturnsSameTaskForSameKey()
    {
        var registry = new InflightDownloadRegistry();
        var tcs = new TaskCompletionSource();

        var first = registry.GetOrAdd("model-a", _ => tcs.Task);
        var second = registry.GetOrAdd("model-a", _ => Task.CompletedTask);

        Assert.Same(first, second);
        tcs.SetResult();
    }

    [Fact]
    public void GetOrAdd_DifferentKeys_ProduceIndependentTasks()
    {
        var registry = new InflightDownloadRegistry();

        var a = registry.GetOrAdd("a", _ => Task.Delay(50));
        var b = registry.GetOrAdd("b", _ => Task.Delay(50));

        Assert.NotSame(a, b);
    }

    [Fact]
    public void Release_LetsSubsequentGetOrAddProduceFreshTask()
    {
        var registry = new InflightDownloadRegistry();
        var firstTcs = new TaskCompletionSource();
        var first = registry.GetOrAdd("k", _ => firstTcs.Task);

        registry.Release("k");

        var secondTcs = new TaskCompletionSource();
        var second = registry.GetOrAdd("k", _ => secondTcs.Task);

        Assert.NotSame(first, second);
        firstTcs.SetResult();
        secondTcs.SetResult();
    }

    [Fact]
    public void Release_OnUnknownKey_DoesNotThrow()
    {
        var registry = new InflightDownloadRegistry();

        registry.Release("never-added");
    }
}
