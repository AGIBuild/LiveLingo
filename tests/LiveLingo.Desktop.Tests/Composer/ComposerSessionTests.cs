using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

/// <summary>The behaviors of the v3 prototype that are not about producing text: the microphone gesture, editing rows, the notices.</summary>
public class ComposerSessionTests
{
    private static async Task<Harness> Translated(string source = "先检查缓存。再更新索引。")
    {
        var h = new Harness();
        h.Vm.SourceText = source;
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        return h;
    }

    // ---- microphone gesture --------------------------------------------------------

    [Fact]
    public async Task ATapOnTheMicrophone_ListensHandsFree_AndAnotherTapStopsIt()
    {
        await using var h = new Harness(output: ComposerOutput.Original);

        await h.Vm.PressMicAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(120));
        await h.Vm.ReleaseMicAsync();

        Assert.True(h.Vm.IsListening);
        Assert.True(h.Vm.IsHandsFree);

        await h.Vm.PressMicAsync();
        await h.Vm.ReleaseMicAsync();

        Assert.False(h.Vm.IsListening);
    }

    [Fact]
    public async Task HoldingTheMicrophone_IsPushToTalk_AndReleasingStopsIt()
    {
        await using var h = new Harness(output: ComposerOutput.Original);

        await h.Vm.PressMicAsync();
        Assert.False(h.Vm.IsHandsFree);
        h.Time.Advance(ComposerViewModel.HoldThreshold + TimeSpan.FromMilliseconds(50));
        await h.Vm.ReleaseMicAsync();

        Assert.False(h.Vm.IsListening);
    }

    [Fact]
    public async Task LosingFocus_StopsPushToTalk_ButNotHandsFreeListening()
    {
        await using var h = new Harness(output: ComposerOutput.Original);
        await h.Vm.StartListeningAsync(handsFree: false);

        await h.Vm.WindowDeactivatedAsync();
        Assert.False(h.Vm.IsListening);

        await h.Vm.StartListeningAsync(handsFree: true);
        await h.Vm.WindowDeactivatedAsync();
        Assert.True(h.Vm.IsListening);
    }

    [Fact]
    public async Task TheRecordingClock_CountsWhileListening_AndResetsAfterwards()
    {
        await using var h = new Harness(output: ComposerOutput.Original);
        await h.Vm.StartListeningAsync();

        h.Time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, h.Vm.RecordingSeconds);
        Assert.Equal(3, h.Vm.StatusInfo.RecordingSeconds);

        await h.Vm.StopListeningAsync();
        Assert.Equal(0, h.Vm.RecordingSeconds);
    }

    [Fact]
    public async Task Escape_StepsBackOneLevelAtATime()
    {
        await using var h = await Translated();
        h.Vm.StartEditCommand.Execute(0);
        Assert.Equal(0, h.Vm.EditingIndex);

        await h.Vm.CancelCommand.ExecuteAsync(null);
        Assert.Equal(-1, h.Vm.EditingIndex);
        Assert.Empty(h.Events);

        await h.Vm.StartListeningAsync();
        await h.Vm.CancelCommand.ExecuteAsync(null);
        Assert.False(h.Vm.IsListening);
        Assert.Empty(h.Events);

        await h.Vm.CancelCommand.ExecuteAsync(null);
        Assert.Contains("close", h.Events);
    }

    // ---- editing a row ----------------------------------------------------------------

    [Fact]
    public async Task EditingARow_StoresTheUsersOwnText()
    {
        await using var h = await Translated();

        h.Vm.StartEditCommand.Execute(0);
        Assert.Equal("T:先检查缓存。", h.Vm.EditText);
        h.Vm.EditText = "Check the cache.";
        h.Vm.SaveEditCommand.Execute(null);

        Assert.Equal(-1, h.Vm.EditingIndex);
        Assert.Equal("Check the cache.", h.Vm.Lines[0].Text);
        Assert.True(h.Vm.Lines[0].IsOwned);
        Assert.False(h.Vm.Lines[1].IsOwned);
    }

    [Fact]
    public async Task CancellingAnEdit_KeepsTheModelsText()
    {
        await using var h = await Translated();

        h.Vm.StartEditCommand.Execute(1);
        h.Vm.EditText = "x";
        h.Vm.CancelEditCommand.Execute(null);

        Assert.Equal("T:再更新索引。", h.Vm.Lines[1].Text);
        Assert.False(h.Vm.Lines[1].IsOwned);
    }

    [Fact]
    public async Task Rows_CannotBeEdited_WithTheOriginalOutput()
    {
        await using var h = await Translated();
        h.Vm.Output = ComposerOutput.Original;

        h.Vm.StartEditCommand.Execute(0);

        Assert.Equal(-1, h.Vm.EditingIndex);
    }

    [Fact]
    public async Task UseOriginal_ReplacesACleanedRowWithWhatWasSaid()
    {
        await using var h = new Harness(output: ComposerOutput.Clean);
        h.Vm.SourceText = "嗯，先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Equal("C:嗯，先检查缓存。", h.Vm.Lines[0].Text);

        h.Vm.UseOriginalCommand.Execute(0);

        Assert.Equal("嗯，先检查缓存。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task Writing_SavesARowThatIsStillBeingEdited()
    {
        await using var h = await Translated("先检查缓存。");
        h.Vm.StartEditCommand.Execute(0);
        h.Vm.EditText = "Check it.";

        await h.Vm.CommitAsync(fromKey: true);

        await h.Injector.Received(1).InjectAsync(Harness.Target, "Check it.", false, Arg.Any<CancellationToken>());
    }

    // ---- draft and destination -----------------------------------------------------------

    [Fact]
    public async Task ANewDraft_EmptiesTheComposer()
    {
        await using var h = await Translated();

        await h.Vm.NewDraftCommand.ExecuteAsync(null);

        Assert.Equal("", h.Vm.SourceText);
        Assert.Empty(h.Vm.Lines);
        Assert.Equal(ComposerStatus.Empty, h.Vm.Status);
        Assert.False(h.Vm.IsWritten);
    }

    [Fact]
    public async Task CopyOnly_PutsTheResultOnTheClipboardInsteadOfWritingIt()
    {
        await using var h = await Translated("先检查缓存。");
        h.Vm.CopyOnly = true;
        Assert.False(h.Vm.HasTarget);

        await h.Vm.CommitAsync(fromKey: true);

        await h.Clipboard.Received(1).SetTextAsync("T:先检查缓存。", Arg.Any<CancellationToken>());
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheWideAndWholeSwitches_ChangeTheLayoutAndTheRoute()
    {
        await using var h = await Translated("先检查缓存。");
        Assert.False(h.Vm.IsWide);
        Assert.False(h.Vm.IsWholeRoute);

        h.Vm.ForceWide = true;
        Assert.True(h.Vm.IsWide);

        h.Vm.ForceWhole = true;
        Assert.True(h.Vm.IsWholeRoute);
        Assert.True(h.Vm.IsWide);
    }

    [Fact]
    public async Task Options_AreExposedToTheWindow()
    {
        await using var h = new Harness(options: new ComposerOptions(true, false, CommitKey.CtrlEnter, false, "Auto"));

        Assert.True(h.Vm.CloudTranslate);
        Assert.False(h.Vm.CloudClean);
        Assert.Equal(CommitKey.CtrlEnter, h.Vm.CommitKey);
        Assert.False(h.Vm.Animations);
        Assert.True(h.Vm.StatusInfo.UsesCloud);
    }

    // ---- notices ------------------------------------------------------------------------------

    [Fact]
    public async Task BlockedAndFinishedActions_RaiseToasts()
    {
        await using var h = new Harness();
        var toasts = new List<ToastKind>();
        h.Vm.ToastRaised += toasts.Add;

        await h.Vm.CommitAsync(fromKey: true);
        Assert.Equal([ToastKind.Empty], toasts);

        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        await h.Vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal([ToastKind.Empty, ToastKind.Copied], toasts);
    }

    [Fact]
    public async Task TheButton_CancelsAQueuedWrite_ButTheKeyDoesNot()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        var toasts = new List<ToastKind>();
        h.Vm.ToastRaised += toasts.Add;
        h.Vm.SourceText = "先检查缓存。";

        await h.Vm.CommitAsync(fromKey: true);
        Assert.True(h.Vm.IsWriteQueued);
        await h.Vm.CommitAsync(fromKey: true);
        Assert.True(h.Vm.IsWriteQueued);

        await h.Vm.CommitAsync(fromKey: false);

        Assert.False(h.Vm.IsWriteQueued);
        Assert.Equal([ToastKind.Cancelled], toasts);
    }

    [Fact]
    public async Task ASlowOutput_OffersTheOriginalWords_AndWaitingAsksAgainLater()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Working);

        h.Time.Advance(ComposerViewModel.SlowAfter + TimeSpan.FromSeconds(1));
        Assert.Equal(BannerKind.SlowProcessing, h.Vm.Banner.Kind);

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.KeepWaiting);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        h.Time.Advance(ComposerViewModel.SlowAfter + TimeSpan.FromSeconds(1));
        Assert.Equal(BannerKind.SlowProcessing, h.Vm.Banner.Kind);

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.UseOriginal);
        Assert.Equal(ComposerOutput.Original, h.Vm.Output);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task TheSlowNotice_GoesAwayWhenTheResultArrives()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Working);
        h.Time.Advance(ComposerViewModel.SlowAfter + TimeSpan.FromSeconds(1));
        Assert.Equal(BannerKind.SlowProcessing, h.Vm.Banner.Kind);

        gate.SetResult(new TranslatedText("Done.", 1, 2));
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task BannerButtons_OpenSettingsAndAskForANewDestination()
    {
        await using var h = new Harness(output: ComposerOutput.Original);
        var sections = new List<string>();
        var retargeted = 0;
        h.Vm.SettingsRequested += sections.Add;
        h.Vm.RetargetRequested += () => retargeted++;

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.OpenInputSettings);
        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.Retarget);
        h.Vm.OpenSettings();

        Assert.Equal(["voice", "general"], sections);
        Assert.Equal(1, retargeted);
    }

    [Fact]
    public async Task TheHideButton_ClosesTheWindowButKeepsTheDraft()
    {
        await using var h = await Translated("先检查缓存。");

        h.Vm.HideCommand.Execute(null);

        Assert.Contains("close", h.Events);
        Assert.Equal("先检查缓存。", h.Vm.SourceText);
    }

    [Fact]
    public async Task Closing_ReturnsTheKeyboardToTheApplicationTheUserCameFrom()
    {
        await using var h = await Translated("先检查缓存。");

        h.Vm.HideCommand.Execute(null);
        await h.Vm.CancelCommand.ExecuteAsync(null);

        h.Tracker.Received(2).Activate(Harness.Target);
    }

    [Fact]
    public async Task Closing_LeavesTheFocusAloneWhenThereIsNoApplicationToGoBackTo()
    {
        await using var withoutTarget = new Harness(hasTarget: false);
        withoutTarget.Vm.HideCommand.Execute(null);

        await using var lost = await Translated("先检查缓存。");
        lost.Tracker.IsAlive(Arg.Any<LiveLingo.Desktop.Platform.TargetWindowInfo>()).Returns(false);
        await lost.Vm.CommitAsync(fromKey: true);
        lost.Vm.HideCommand.Execute(null);

        withoutTarget.Tracker.DidNotReceiveWithAnyArgs().Activate(default!);
        lost.Tracker.DidNotReceiveWithAnyArgs().Activate(default!);
        Assert.Contains("close", withoutTarget.Events);
    }
}
