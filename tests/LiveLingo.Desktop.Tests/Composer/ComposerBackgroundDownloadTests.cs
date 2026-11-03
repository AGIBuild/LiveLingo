using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

/// <summary>
/// Downloads belong to the application, not to a composer window: sending one to the background keeps the progress
/// in sight, and closing the window neither stops it nor lets the closed composer act when it ends.
/// </summary>
public sealed class ComposerBackgroundDownloadTests
{
    private static void Report(Harness h, ModelDescriptor model, double percent)
    {
        var state = new ModelDownloadState(model.Id, ModelDownloadStatus.Downloading, percent, null);
        h.Downloads.GetState(model.Id).Returns(state);
        h.Downloads.StateChanged += NSubstitute.Raise.Event<Action<ModelDownloadState>>(state);
    }

    [Fact]
    public async Task SendingTheDownloadToTheBackground_HidesTheBanner_AndItStaysHidden()
    {
        await using var h = new Harness(modelsInstalled: false);
        Report(h, h.TranslatorModel, 30);
        Assert.Equal(BannerKind.Downloading, h.Vm.Banner.Kind);

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Background);
        Report(h, h.TranslatorModel, 60);
        h.Vm.SourceText = "先检查缓存。";

        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.InRange(h.Vm.DownloadProgress ?? -1, 1, 99);
    }

    [Fact]
    public async Task WhileDownloadingInTheBackground_TheFooterCarriesTheProgress_NotTheBanner()
    {
        await using var h = new Harness(modelsInstalled: false);
        Report(h, h.TranslatorModel, 30);
        Assert.Equal(BannerKind.Downloading, h.Vm.Banner.Kind);
        Assert.NotNull(h.Vm.DownloadProgress);

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Background);

        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.NotNull(h.Vm.DownloadProgress);
    }

    [Fact]
    public async Task WhenTheDownloadEnds_TheProgressGoesAway()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(call =>
        {
            h.Models.HasAllExpectedLocalAssets(call.Arg<ModelDescriptor>()).Returns(true);
            return Task.CompletedTask;
        });
        Report(h, h.TranslatorModel, 50);
        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Background);

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        Assert.Null(h.Vm.DownloadProgress);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.True(h.Vm.ModelsReady);
    }

    [Fact]
    public async Task ADownloadThatFails_ShowsTheFailure_EvenAfterGoingToTheBackground()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.GetState(Arg.Any<string>()).Returns(call =>
            new ModelDownloadState(call.Arg<string>(), ModelDownloadStatus.Failed, 0, "disk full"));
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);
        Report(h, h.TranslatorModel, 50);
        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Background);

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        Assert.Equal(BannerKind.DownloadFailed, h.Vm.Banner.Kind);
        Assert.Null(h.Vm.DownloadProgress);
    }

    [Fact]
    public async Task ClosingTheComposerMidDownload_LetsTheDownloadFinish_AndTheClosedComposerDoesNothing()
    {
        var h = new Harness(modelsInstalled: false, output: ComposerOutput.Original);
        var gate = new TaskCompletionSource();
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(async call =>
        {
            await gate.Task;
            h.Models.HasAllExpectedLocalAssets(call.Arg<ModelDescriptor>()).Returns(true);
        });
        await h.Vm.StartListeningAsync();
        var download = h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        await h.Vm.DisposeAsync();
        gate.SetResult();
        await download;

        foreach (var model in ModelCatalog.Speech)
            await h.Downloads.Received(1).StartAsync(model);
        Assert.Equal(0, h.Voice.Starts);
        Assert.False(h.Vm.IsListening);
        await h.Cleanup.DisposeAsync();
        await h.Scheduler.DisposeAsync();
    }

    [Fact]
    public async Task ModelsThatBecomeNeededMidDownload_AreDownloadedToo_WithoutAFalseFailure()
    {
        await using var h = new Harness(modelsInstalled: false);
        var gate = new TaskCompletionSource();
        var first = true;
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(async call =>
        {
            var model = call.Arg<ModelDescriptor>();
            if (first)
            {
                first = false;
                await gate.Task;
            }
            h.Models.HasAllExpectedLocalAssets(model).Returns(true);
        });
        var download = h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        await h.Vm.StartListeningAsync();
        gate.SetResult();
        await download;

        foreach (var model in ModelCatalog.Speech)
            await h.Downloads.Received(1).StartAsync(model);
        Assert.NotEqual(BannerKind.DownloadFailed, h.Vm.Banner.Kind);
        Assert.True(h.Vm.IsListening);
    }

    [Fact]
    public async Task ADownloadThatInstallsNothing_EndsWithAFailure_NotAnEndlessLoop()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        Assert.Equal(BannerKind.DownloadFailed, h.Vm.Banner.Kind);
        Assert.Null(h.Vm.DownloadProgress);
        Assert.False(h.Vm.ModelsReady);
    }

    [Fact]
    public async Task ASummonThatFindsTheDownloadRunning_JoinsItInTheBackground()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.GetState(h.TranslatorModel.Id).Returns(
            new ModelDownloadState(h.TranslatorModel.Id, ModelDownloadStatus.Downloading, 40, null));
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(new TaskCompletionSource().Task);

        h.Vm.Begin(new ComposerStart(
            ComposerOutput.Translate, TargetLanguage.English, ComposerOutput.Clean, TargetLanguage.English, null));

        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.NotNull(h.Vm.DownloadProgress);
        await h.Downloads.Received(1).StartAsync(h.Runtime);
    }
}
