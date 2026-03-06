using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class StubModelManagerTests
{
    private readonly StubModelManager _mgr = new();

    [Fact]
    public async Task EnsureModelAsync_CompletesSuccessfully()
    {
        var desc = new ModelDescriptor("test", "Test", "http://x", 100, ModelType.Translation);
        await _mgr.EnsureModelAsync(desc, null, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureModelAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var desc = new ModelDescriptor("test", "Test", "http://x", 100, ModelType.Translation);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _mgr.EnsureModelAsync(desc, null, cts.Token));
    }

    [Fact]
    public void ListInstalled_ReturnsEmpty()
    {
        Assert.Empty(_mgr.ListInstalled());
    }

    [Fact]
    public async Task DeleteModelAsync_CompletesSuccessfully()
    {
        await _mgr.DeleteModelAsync("non-existent", CancellationToken.None);
    }

    [Fact]
    public void GetTotalDiskUsage_ReturnsZero()
    {
        Assert.Equal(0, _mgr.GetTotalDiskUsage());
    }
}
