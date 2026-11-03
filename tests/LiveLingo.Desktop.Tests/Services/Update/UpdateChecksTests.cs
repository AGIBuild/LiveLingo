using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Tests.Composer;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Desktop.Tests.Services.Update;

public sealed class UpdateChecksTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(4);

    private sealed class FakeService : IUpdateService
    {
        public bool CanUpdate { get; set; } = true;
        public string? DownloadPage { get; set; }
        public string? ReadyVersion { get; private set; }
        public Func<Task<string?>> Find { get; set; } = () => Task.FromResult<string?>(null);
        public Func<Action<int>, Task> Download { get; set; } = _ => Task.CompletedTask;
        public int Finds { get; private set; }
        public int Restarts { get; private set; }
        public int ExitInstalls { get; private set; }
        private string? _found;

        public async Task<string?> FindAsync(CancellationToken ct = default)
        {
            Finds++;
            ct.ThrowIfCancellationRequested();
            return _found = await Find();
        }

        public async Task DownloadAsync(Action<int> progress, CancellationToken ct = default)
        {
            await Download(progress);
            ReadyVersion = _found;
        }

        public void ApplyAndRestart() => Restarts++;

        public void ApplyOnExit() => ExitInstalls++;
    }

    private readonly FakeService _service = new();
    private readonly FakeClock _time = new();

    private UpdateChecks Create() => new(_service, Interval, NullLogger.Instance, _time);

    private static List<UpdateState> Record(UpdateChecks checks)
    {
        var seen = new List<UpdateState>();
        checks.StateChanged += () =>
        {
            lock (seen)
                seen.Add(checks.State);
        };
        return seen;
    }

    [Fact]
    public async Task Start_ChecksAtOnce_AndThenEveryInterval()
    {
        using var checks = Create();

        checks.Start();
        await Wait.Until(() => _service.Finds == 1);

        _time.Advance(Interval);
        await Wait.Until(() => _service.Finds == 2);
    }

    [Fact]
    public async Task ACopyThatCannotUpdate_SaysSo_AndNeverLooks()
    {
        _service.CanUpdate = false;
        using var checks = Create();
        var seen = Record(checks);

        checks.Start();
        await checks.CheckNowAsync(TestContext.Current.CancellationToken);
        _time.Advance(Interval);

        Assert.Equal(UpdatePhase.Unsupported, checks.State.Phase);
        Assert.Equal(0, _service.Finds);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task TheLatestVersion_IsReportedWithTheTimeOfTheCheck()
    {
        using var checks = Create();
        var seen = Record(checks);

        await checks.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal([UpdatePhase.Checking, UpdatePhase.UpToDate], seen.Select(state => state.Phase));
        Assert.Equal(_time.GetUtcNow(), checks.State.At);
    }

    [Fact]
    public async Task ANewerVersion_IsDownloadedWithItsProgress_AndAnnouncedOnce()
    {
        _service.Find = () => Task.FromResult<string?>("2.2.0");
        _service.Download = progress =>
        {
            progress(0);
            progress(40);
            progress(40);
            progress(250);
            return Task.CompletedTask;
        };
        using var checks = Create();
        var seen = Record(checks);
        var ready = new List<string>();
        checks.Ready += ready.Add;

        await checks.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [UpdatePhase.Checking, UpdatePhase.Downloading, UpdatePhase.Downloading, UpdatePhase.Downloading, UpdatePhase.Ready],
            seen.Select(state => state.Phase));
        Assert.Equal([0, 40, 100], seen.Where(state => state.Phase == UpdatePhase.Downloading).Select(state => state.Percent));
        Assert.Equal(new UpdateState(UpdatePhase.Ready, "2.2.0"), checks.State);
        Assert.Equal(["2.2.0"], ready);
        Assert.Equal("2.2.0", checks.ReadyVersion);
    }

    [Fact]
    public async Task AfterADownload_TheChecksEnd()
    {
        _service.Find = () => Task.FromResult<string?>("2.2.0");
        using var checks = Create();

        checks.Start();
        await Wait.Until(() => checks.State.Phase == UpdatePhase.Ready);
        _time.Advance(Interval);
        await checks.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _service.Finds);
    }

    [Fact]
    public async Task AFailure_BecomesTheState_AndTheNextIntervalTriesAgain()
    {
        var attempt = 0;
        _service.Find = () => ++attempt == 1
            ? Task.FromException<string?>(new HttpRequestException("offline"))
            : Task.FromResult<string?>(null);
        using var checks = Create();

        checks.Start();
        await Wait.Until(() => checks.State.Phase == UpdatePhase.Failed);

        Assert.Equal("offline", checks.State.Error);
        Assert.Equal(_time.GetUtcNow(), checks.State.At);

        _time.Advance(Interval);
        await Wait.Until(() => checks.State.Phase == UpdatePhase.UpToDate);
    }

    [Fact]
    public async Task ACheckWhileOneRuns_DoesNotStartAnother()
    {
        var gate = new TaskCompletionSource<string?>();
        _service.Find = () => gate.Task;
        using var checks = Create();

        var first = checks.CheckNowAsync(TestContext.Current.CancellationToken);
        await Wait.Until(() => checks.State.Phase == UpdatePhase.Checking);
        await checks.CheckNowAsync(TestContext.Current.CancellationToken);
        gate.SetResult(null);
        await first;

        Assert.Equal(1, _service.Finds);
    }

    [Theory]
    [InlineData(UpdatePhase.Checking, true)]
    [InlineData(UpdatePhase.Downloading, true)]
    [InlineData(UpdatePhase.Idle, false)]
    [InlineData(UpdatePhase.Ready, false)]
    [InlineData(UpdatePhase.Failed, false)]
    public void OnlyACheckAndADownload_AreBusy(UpdatePhase phase, bool busy) =>
        Assert.Equal(busy, new UpdateState(phase).Busy);

    [Fact]
    public async Task ACancelledCheck_LeavesNoResult()
    {
        using var checks = Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await checks.CheckNowAsync(cancelled.Token);

        Assert.Equal(UpdatePhase.Idle, checks.State.Phase);
    }

    [Fact]
    public async Task ADownloadedVersion_IsNotLookedForAgain()
    {
        _service.Find = () => Task.FromResult<string?>("2.2.0");
        using var checks = Create();
        await checks.CheckNowAsync(TestContext.Current.CancellationToken);

        await checks.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, _service.Finds);
    }

    [Fact]
    public async Task RestartingAndQuitting_InstallOnlyTheDownloadedVersion()
    {
        using var checks = Create();

        checks.ApplyAndRestart();
        checks.ApplyOnExit();
        Assert.Equal(0, _service.Restarts + _service.ExitInstalls);

        _service.Find = () => Task.FromResult<string?>("2.2.0");
        await checks.CheckNowAsync(TestContext.Current.CancellationToken);
        checks.ApplyAndRestart();
        checks.ApplyOnExit();

        Assert.Equal(1, _service.Restarts);
        Assert.Equal(1, _service.ExitInstalls);
    }
}
