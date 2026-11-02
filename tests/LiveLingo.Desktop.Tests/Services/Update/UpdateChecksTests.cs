using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Tests.Composer;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services.Update;

public sealed class UpdateChecksTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private readonly IUpdateService _service = Substitute.For<IUpdateService>();
    private readonly FakeClock _time = new();

    public UpdateChecksTests()
    {
        // A substitute answers a string property with "", which would read as a downloaded version.
        _service.ReadyVersion.Returns((string?)null);
    }

    private UpdateChecks Create() => new(_service, Interval, NullLogger.Instance, _time);

    private async Task Settled() => await Task.Delay(50, TestContext.Current.CancellationToken);

    private int Checks => _service.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IUpdateService.CheckAndDownloadAsync));

    [Fact]
    public async Task Start_ChecksAtOnce_AndThenEveryInterval()
    {
        using var checks = Create();

        checks.Start();
        await Wait.Until(() => Checks == 1);

        _time.Advance(Interval);
        await Wait.Until(() => Checks == 2);
    }

    [Fact]
    public async Task ADownloadedVersion_IsAnnouncedOnce_AndEndsTheChecks()
    {
        _service.CheckAndDownloadAsync(Arg.Any<CancellationToken>()).Returns(true);
        _service.ReadyVersion.Returns(null, "2.0.1");
        using var checks = Create();
        var ready = new List<string>();
        checks.Ready += ready.Add;

        checks.Start();
        await Wait.Until(() => ready.Count == 1);
        _time.Advance(Interval);
        await Settled();

        Assert.Equal(["2.0.1"], ready);
        await _service.Received(1).CheckAndDownloadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABackgroundFailure_IsNotFatal_AndTheNextIntervalTriesAgain()
    {
        _service.CheckAndDownloadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException("offline")), Task.FromResult(false));
        using var checks = Create();

        checks.Start();
        await Wait.Until(() => Checks == 1);
        _time.Advance(Interval);
        await Wait.Until(() => Checks == 2);

        await _service.Received(2).CheckAndDownloadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACheckTheUserAsksFor_ReportsTheFailure()
    {
        _service.CheckAndDownloadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new HttpRequestException("offline")));
        using var checks = Create();

        await Assert.ThrowsAsync<HttpRequestException>(() => checks.CheckNowAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACheckTheUserAsksFor_DoesNotLookAgain_WhenAVersionIsAlreadyReady()
    {
        _service.ReadyVersion.Returns("2.0.1");
        using var checks = Create();

        Assert.True(await checks.CheckNowAsync(TestContext.Current.CancellationToken));

        await _service.DidNotReceive().CheckAndDownloadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoNewerVersion_IsNotAnnounced()
    {
        _service.CheckAndDownloadAsync(Arg.Any<CancellationToken>()).Returns(false);
        using var checks = Create();
        var ready = new List<string>();
        checks.Ready += ready.Add;

        Assert.False(await checks.CheckNowAsync(TestContext.Current.CancellationToken));

        Assert.Empty(ready);
    }

    [Fact]
    public void QuittingInstallsTheDownloadedVersion_AndDoesNothingWithout()
    {
        using var checks = Create();

        checks.ApplyOnExit();
        _service.DidNotReceive().ApplyOnExit();

        _service.ReadyVersion.Returns("2.0.1");
        checks.ApplyOnExit();
        _service.Received(1).ApplyOnExit();
    }
}
