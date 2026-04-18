using LiveLingo.Core.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LiveLingo.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="InProcessModelDownloadCoordinator"/>. The coordinator
/// is the single source of truth for model-download lifecycles, so these tests
/// pin down the contract that <c>ModelItemViewModel</c> relies on: identical
/// concurrent starts collapse, terminal states replace in-flight ones, and
/// <see cref="IModelDownloadCoordinator.GetState"/> reflects installation even
/// for models the coordinator never downloaded itself.
/// </summary>
public class InProcessModelDownloadCoordinatorTests
{
    private static readonly ModelDescriptor Descriptor = new(
        "coord-test", "Coord Test",
        "https://example.com/m.bin",
        1024, ModelType.Translation);

    [Fact]
    public async Task StartAsync_Success_PublishesDownloadingThenInstalled()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var coord = new InProcessModelDownloadCoordinator(mm);
        var events = new List<ModelDownloadState>();
        coord.StateChanged += events.Add;

        await coord.StartAsync(Descriptor);

        Assert.True(events.Count >= 2, $"expected at least 2 state events, got {events.Count}");
        Assert.Equal(ModelDownloadStatus.Downloading, events[0].Status);
        Assert.Equal(ModelDownloadStatus.Installed, events[^1].Status);
        Assert.Equal(100, events[^1].Percentage);
    }

    [Fact]
    public async Task StartAsync_DuplicateInFlight_CollapsesToSameTask()
    {
        var mm = Substitute.For<IModelManager>();
        var gate = new TaskCompletionSource();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        var coord = new InProcessModelDownloadCoordinator(mm);

        var first = coord.StartAsync(Descriptor);
        var second = coord.StartAsync(Descriptor);

        Assert.Same(first, second);

        gate.SetResult();
        await first;

        await mm.Received(1).EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_InFlight_PublishesCancelledAndDoesNotThrow()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var ct = call.ArgAt<CancellationToken>(2);
                await Task.Delay(5_000, ct);
            });

        var coord = new InProcessModelDownloadCoordinator(mm);
        var events = new List<ModelDownloadState>();
        coord.StateChanged += events.Add;

        var run = coord.StartAsync(Descriptor);
        await Task.Delay(50);
        coord.Cancel(Descriptor.Id);
        await run;

        Assert.Contains(events, s => s.Status == ModelDownloadStatus.Cancelled);
    }

    [Fact]
    public async Task StartAsync_AuthorizationException_PublishesFailedWithKnownCode()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new ModelDownloadAuthorizationException("nope"));

        var coord = new InProcessModelDownloadCoordinator(mm);
        ModelDownloadState? last = null;
        coord.StateChanged += s => last = s;

        await coord.StartAsync(Descriptor);

        Assert.NotNull(last);
        Assert.Equal(ModelDownloadStatus.Failed, last!.Status);
        Assert.Equal(ModelDownloadErrorCodes.HuggingFaceAuthorization, last.ErrorMessage);
    }

    [Fact]
    public async Task StartAsync_GenericException_PublishesFailedWithMessage()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        var coord = new InProcessModelDownloadCoordinator(mm);
        ModelDownloadState? last = null;
        coord.StateChanged += s => last = s;

        await coord.StartAsync(Descriptor);

        Assert.NotNull(last);
        Assert.Equal(ModelDownloadStatus.Failed, last!.Status);
        Assert.Equal("boom", last.ErrorMessage);
    }

    [Fact]
    public void GetState_UnknownModel_ReflectsInstallationFromManager()
    {
        var mm = Substitute.For<IModelManager>();
        mm.ListInstalled().Returns([
            new InstalledModel("coord-test", "Coord Test", "/tmp", 1024, ModelType.Translation, DateTime.UtcNow)
        ]);
        var coord = new InProcessModelDownloadCoordinator(mm);

        var state = coord.GetState("coord-test");

        Assert.Equal(ModelDownloadStatus.Installed, state.Status);
        Assert.Equal(100, state.Percentage);
    }

    [Fact]
    public void GetState_UnknownAndNotInstalled_IsIdle()
    {
        var mm = Substitute.For<IModelManager>();
        mm.ListInstalled().Returns([]);
        var coord = new InProcessModelDownloadCoordinator(mm);

        var state = coord.GetState("coord-test");

        Assert.Equal(ModelDownloadStatus.Idle, state.Status);
        Assert.Equal(0, state.Percentage);
    }

    [Fact]
    public void NotifyDeleted_PublishesIdleState()
    {
        var mm = Substitute.For<IModelManager>();
        var coord = new InProcessModelDownloadCoordinator(mm);
        ModelDownloadState? last = null;
        coord.StateChanged += s => last = s;

        coord.NotifyDeleted(Descriptor.Id);

        Assert.NotNull(last);
        Assert.Equal(ModelDownloadStatus.Idle, last!.Status);
    }

    [Fact]
    public void Cancel_UnknownModel_DoesNotThrow()
    {
        var mm = Substitute.For<IModelManager>();
        var coord = new InProcessModelDownloadCoordinator(mm);

        coord.Cancel("does-not-exist");
    }

    [Fact]
    public async Task StartAsync_ReportsProgress()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var progress = call.ArgAt<IProgress<ModelDownloadProgress>?>(1);
                progress?.Report(new ModelDownloadProgress("coord-test", 256, 1024));
                await Task.Yield();
            });

        var coord = new InProcessModelDownloadCoordinator(mm);
        var seen = new List<double>();
        coord.StateChanged += s => { if (s.IsDownloading) seen.Add(s.Percentage); };

        await coord.StartAsync(Descriptor);

        Assert.Contains(25d, seen);
    }
}
