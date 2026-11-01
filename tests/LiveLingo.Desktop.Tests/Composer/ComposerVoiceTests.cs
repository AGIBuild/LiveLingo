using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerVoiceTests
{
    private static Harness Create(bool modelsInstalled = true, ComposerOutput output = ComposerOutput.Original) =>
        new(modelsInstalled, output: output);

    [Fact]
    public async Task Starting_OpensTheVoiceInput_AndMarksTheComposerAsListening()
    {
        await using var h = Create();

        await h.Vm.StartListeningAsync();

        Assert.Equal(1, h.Voice.Starts);
        Assert.True(h.Vm.IsListening);
        Assert.Equal(ComposerStatus.Listening, h.Vm.Status);
        Assert.False(h.Vm.IsEditable);
    }

    [Fact]
    public async Task WhileListening_TheSourcePane_ShowsWhatWasRecognized_WithoutBeingReparsed()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();

        h.Voice.Say("先改接口。");
        h.Voice.Hear("然后补");

        await Wait.Until(() => h.Vm.SourceText == "先改接口。然后补");
        Assert.Equal(["先改接口。"], h.Session.Read(d => d.Units.Select(u => u.Raw).ToList()));
        Assert.Equal(["先改接口。"], h.Vm.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Stopping_KeepsTheRecognizedText_MakesTheSourceEditable_AndLaterEditsReconcile()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();
        h.Voice.Say("先改接口。", "再补测试。");
        await Wait.Until(() => h.Vm.SourceText == "先改接口。再补测试。");

        await h.Vm.StopListeningAsync();

        Assert.False(h.Vm.IsListening);
        Assert.True(h.Vm.IsEditable);
        Assert.Equal(1, h.Voice.Stops);

        h.Vm.SourceText = "先改接口。再补测试。最后发版。";

        var raws = h.Session.Read(d => d.Units.Select(u => u.Raw).ToList());
        Assert.Equal(["先改接口。", "再补测试。", "最后发版。"], raws);
    }

    [Fact]
    public async Task ATap_StartsHandsFree_AndAnotherTapStopsIt()
    {
        await using var h = Create();

        await h.Vm.PressMicAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(120));
        await h.Vm.ReleaseMicAsync();
        Assert.True(h.Vm.IsListening);

        await h.Vm.PressMicAsync();
        Assert.False(h.Vm.IsListening);
        await h.Vm.ReleaseMicAsync();
        Assert.False(h.Vm.IsListening);
        Assert.Equal(1, h.Voice.Starts);
        Assert.Equal(1, h.Voice.Stops);
    }

    [Fact]
    public async Task AHold_IsPushToTalk_AndReleasingStopsListening()
    {
        await using var h = Create();

        await h.Vm.PressMicAsync();
        h.Time.Advance(ComposerViewModel.HoldThreshold + TimeSpan.FromMilliseconds(50));
        await h.Vm.ReleaseMicAsync();

        Assert.False(h.Vm.IsListening);
        Assert.Equal(1, h.Voice.Stops);
    }

    [Fact]
    public async Task ReleasingAHoldWhileTheModelsAreStillLoading_StopsAsSoonAsListeningBegins()
    {
        await using var h = Create();
        h.Voice.StartGate = new TaskCompletionSource();

        var press = h.Vm.PressMicAsync();
        Assert.True(h.Vm.IsStartingListening);
        Assert.Equal(ComposerStatus.Starting, h.Vm.Status);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.Vm.ReleaseMicAsync();
        h.Voice.StartGate.SetResult();
        await press;

        Assert.False(h.Vm.IsListening);
        Assert.False(h.Vm.IsStartingListening);
        Assert.Equal(1, h.Voice.Stops);
    }

    [Fact]
    public async Task ADeniedMicrophone_ShowsABanner_AndNeverStartsTheVoiceInput()
    {
        await using var h = Create();
        h.Permission.Status.Returns(MicrophoneAccess.Denied);

        await h.Vm.StartListeningAsync();

        Assert.Equal(BannerKind.MicrophoneDenied, h.Vm.Banner.Kind);
        Assert.Equal(0, h.Voice.Starts);
        h.Vm.OpenMicrophoneSettingsCommand.Execute(null);
        h.Permission.Received(1).OpenSettings();
    }

    [Fact]
    public async Task AnUndeterminedPermission_StillStarts_SoTheSystemCanAsk()
    {
        await using var h = Create();
        h.Permission.Status.Returns(MicrophoneAccess.NotDetermined);

        await h.Vm.StartListeningAsync();

        Assert.True(h.Vm.IsListening);
    }

    [Fact]
    public async Task MissingSpeechModels_AskForConsent_AndListeningWaitsForTheDownload()
    {
        await using var h = Create(modelsInstalled: false);

        await h.Vm.StartListeningAsync();

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.Equal(0, h.Voice.Starts);
        Assert.False(h.Vm.IsListening);
    }

    [Fact]
    public async Task Downloading_TheSpeechModels_ThenStartsListeningAutomatically()
    {
        await using var h = Create(modelsInstalled: false);
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(call =>
        {
            h.Models.HasAllExpectedLocalAssets(call.Arg<ModelDescriptor>()).Returns(true);
            return Task.CompletedTask;
        });
        await h.Vm.StartListeningAsync();

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        foreach (var model in ModelCatalog.Speech)
            await h.Downloads.Received(1).StartAsync(model);
        Assert.True(h.Vm.IsListening);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task ANoMicrophoneError_IsABanner_NotACrash()
    {
        await using var h = Create();
        h.Voice.StartFailure = new InvalidOperationException("No default input device.");

        await h.Vm.StartListeningAsync();

        Assert.Equal(BannerKind.NoMicrophone, h.Vm.Banner.Kind);
        Assert.Equal("No default input device.", h.Vm.Banner.Detail);
        Assert.False(h.Vm.IsListening);
        Assert.False(h.Vm.IsStartingListening);
    }

    [Fact]
    public async Task ModelsThatDisappeared_AreReportedAsMissing()
    {
        await using var h = Create();
        h.Voice.StartFailure = new ModelNotReadyException(ModelType.SpeechToText, "x", "missing", "download");

        await h.Vm.StartListeningAsync();

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task Escape_StopsListeningFirst_AndOnlyThenCloses()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();

        await h.Vm.CancelCommand.ExecuteAsync(null);
        Assert.False(h.Vm.IsListening);
        Assert.Empty(h.Events);

        await h.Vm.CancelCommand.ExecuteAsync(null);
        Assert.Equal(["close"], h.Events);
    }

    [Fact]
    public async Task Write_WhileListening_StopsListening_ThenWritesTheSettledText()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();
        h.Voice.Say("先改接口。");
        await Wait.Until(() => h.Vm.Lines.Count == 1);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        Assert.False(h.Vm.IsListening);
        await h.Injector.Received(1).InjectAsync(Harness.Target, "先改接口。", false, Arg.Any<CancellationToken>());
        Assert.Equal(["completed"], h.Events);
    }

    [Fact]
    public async Task EditingIsLocked_WhileTheMicrophoneIsOpen()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();
        h.Voice.Say("先改接口。");
        await Wait.Until(() => h.Vm.SourceText == "先改接口。");

        Assert.False(h.Vm.IsEditable);
        Assert.False(h.Vm.CanWrite);
    }

    [Fact]
    public async Task Translating_WhileSpeaking_ProducesTranslationsOfFinishedSentences()
    {
        await using var h = Create(output: ComposerOutput.Translate);
        await h.Vm.StartListeningAsync();

        h.Voice.Say("先改接口。");

        await Wait.Until(() => h.Vm.Lines.Count == 1 && h.Vm.Lines[0].State == LineState.Ready);
        Assert.Equal("T:先改接口。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task CleanOutput_ShowsTheCleanedSentences_ProducedByTheCleanupScheduler()
    {
        await using var h = Create(output: ComposerOutput.Clean);
        await h.Vm.StartListeningAsync();

        h.Voice.Say("呃，先改接口。", "再补测试。");

        await Wait.Until(() => h.Vm.Lines.Count == 2 && h.Vm.Lines[0].State == LineState.Ready);
        Assert.Equal("C:呃，先改接口。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task CleanIsTheDefaultOutput()
    {
        var h = new Harness(output: ComposerOutput.Clean);
        await using var _ = h;

        Assert.Equal(ComposerOutput.Clean, h.Vm.Output);
        Assert.Equal(ComposerOutput.Clean, new ComposerViewModel(
            h.Session, h.Scheduler,
            new ComposerModelPlan([], [], [], []),
            h.Models, h.Downloads, h.Platform, h.Voice, h.Transcriber, [], ComposerOptions.Default, null).Output);
    }

    [Fact]
    public async Task Retry_RetriesTheCleanupOfAFailedSentence_WhenCleanIsSelected()
    {
        await using var h = Create(output: ComposerOutput.Clean);
        var calls = 0;
        h.Cleaner.Handler = (request, _) => ++calls == 1
            ? Task.FromException<string>(new InvalidDataException("bad"))
            : Task.FromResult("C:" + request.Target);
        h.Vm.SourceText = "先改接口。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Failed);

        h.Vm.RetryCommand.Execute(0);

        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Equal("C:先改接口。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task Dispose_StopsTheVoiceInput()
    {
        var h = Create();
        await h.Vm.StartListeningAsync();

        await h.DisposeAsync();

        Assert.True(h.Voice.Stops >= 1);
    }

    [Fact]
    public async Task ADeadMicrophone_StopsListening_ShowsTheNoSoundBanner_AndListeningAgainClearsIt()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();

        h.Voice.RaiseNoSignal(true);
        await Wait.Until(() => h.Vm.Banner.Kind == BannerKind.NoSound);
        Assert.False(h.Vm.IsListening);

        await h.Vm.StartListeningAsync();
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.True(h.Vm.IsListening);
    }

    [Fact]
    public async Task NoSound_IsIgnoredWhenNotListening_AndDoesNotReplaceAnotherBanner()
    {
        await using var h = Create();

        h.Voice.RaiseNoSignal(true);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);

        h.Permission.Status.Returns(MicrophoneAccess.Denied);
        await h.Vm.StartListeningAsync();
        h.Voice.RaiseNoSignal(true);
        Assert.Equal(BannerKind.MicrophoneDenied, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task NoSound_KeepsWhatWasHeard_AndDismissingTheBannerClearsIt()
    {
        await using var h = Create();
        await h.Vm.StartListeningAsync();
        h.Voice.Say("先检查缓存。");
        h.Voice.RaiseNoSignal(true);
        await Wait.Until(() => h.Vm.Banner.Kind == BannerKind.NoSound);

        Assert.Contains("先检查缓存。", h.Vm.SourceText);

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Dismiss);

        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task SentencesThatKeptTheirWording_CarryTheReason_WhenCleanIsSelected()
    {
        await using var h = Create(output: ComposerOutput.Clean);
        h.Cleaner.Handler = (request, _) => Task.FromResult(request.Target.Contains("8080") ? "端口改一下。" : request.Target);

        h.Vm.SourceText = "端口改成 8080。再补测试。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.NotEqual("", h.Vm.Lines[0].Warning);
        Assert.Equal("", h.Vm.Lines[1].Warning);
        Assert.Equal("端口改成 8080。再补测试。", h.Vm.BuildExport(ExportFormat.Txt));
    }
}
