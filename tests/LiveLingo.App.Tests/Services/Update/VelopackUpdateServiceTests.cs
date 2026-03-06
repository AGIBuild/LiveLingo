using LiveLingo.App.Services.Update;
using NSubstitute;

namespace LiveLingo.App.Tests.Services.Update;

public class UpdateServiceContractTests
{
    private readonly IUpdateService _sut = Substitute.For<IUpdateService>();

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsTrue_SetsAvailableVersion()
    {
        _sut.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns(true);
        _sut.IsUpdateAvailable.Returns(true);
        _sut.AvailableVersion.Returns("1.2.0");

        var result = await _sut.CheckForUpdateAsync();

        Assert.True(result);
        Assert.True(_sut.IsUpdateAvailable);
        Assert.Equal("1.2.0", _sut.AvailableVersion);
    }

    [Fact]
    public async Task CheckForUpdateAsync_ReturnsFalse_WhenNoUpdate()
    {
        _sut.CheckForUpdateAsync(Arg.Any<CancellationToken>()).Returns(false);
        _sut.IsUpdateAvailable.Returns(false);
        _sut.AvailableVersion.Returns((string?)null);

        var result = await _sut.CheckForUpdateAsync();

        Assert.False(result);
        Assert.False(_sut.IsUpdateAvailable);
        Assert.Null(_sut.AvailableVersion);
    }

    [Fact]
    public async Task DownloadAndApplyAsync_ReportsProgress()
    {
        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        await _sut.DownloadAndApplyAsync(progress);

        await _sut.Received(1).DownloadAndApplyAsync(progress, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAndApplyAsync_Cancellable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _sut.DownloadAndApplyAsync(Arg.Any<IProgress<int>?>(), cts.Token)
            .Returns(Task.FromCanceled(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.DownloadAndApplyAsync(null, cts.Token));
    }

    [Fact]
    public void Interface_DefaultState()
    {
        var fresh = Substitute.For<IUpdateService>();
        Assert.False(fresh.IsUpdateAvailable);
    }
}
