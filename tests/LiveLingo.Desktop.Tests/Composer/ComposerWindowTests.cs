using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Localization;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerWindowTests
{
    private static (ComposerWindow Window, Harness Harness) Open(
        bool modelsInstalled = true, ComposerOutput output = ComposerOutput.Translate, ComposerOptions? options = null)
    {
        var h = new Harness(modelsInstalled, notifyOn: SynchronizationContext.Current, output: output, options: options);
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        loc.T(Arg.Any<string>(), Arg.Any<object[]>()).Returns(call => call.ArgAt<string>(0));
        var window = new ComposerWindow(h.Vm, new ComposerTexts(loc), Harness.Target, new RecentDrafts());
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, h);
    }

    private static T Find<T>(ComposerWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    [AvaloniaFact]
    public async Task OpensWithTheDestinationAndAnEmptyComposer()
    {
        var (window, h) = Open();
        await using var _ = h;

        Assert.Equal("Slack", Find<TextBlock>(window, "DestText").Text);
        Assert.Equal("composer.privacy.local", Find<TextBlock>(window, "PrivacyText").Text);
        Assert.False(Find<Border>(window, "ResultPane").IsVisible);
        Assert.False(Find<Border>(window, "BannerBox").IsVisible);
        Assert.True(Find<StackPanel>(window, "HintPanel").IsVisible);
        Assert.False(Find<Button>(window, "CommitButton").IsEnabled);
        Assert.False(Find<Button>(window, "CopyButton").IsEnabled);
        Assert.Equal("composer.placeholder", Find<TextBox>(window, "SourceBox").PlaceholderText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TypingShowsTheResult_AndEnablesWritingAndCopying()
    {
        var (window, h) = Open();
        await using var _ = h;

        Find<TextBox>(window, "SourceBox").Text = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.True(Find<Border>(window, "ResultPane").IsVisible);
        Assert.False(Find<StackPanel>(window, "HintPanel").IsVisible);
        Assert.True(Find<Button>(window, "CommitButton").IsEnabled);
        Assert.True(Find<Button>(window, "CopyButton").IsEnabled);
        Assert.Equal("composer.status.ready.local", Find<TextBlock>(window, "StatusText").Text);
        Assert.Equal("composer.commit.write", Find<TextBlock>(window, "CommitText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheOriginalOutput_HidesTheResultPane()
    {
        var (window, h) = Open(output: ComposerOutput.Original);
        await using var _ = h;

        Find<TextBox>(window, "SourceBox").Text = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.False(Find<Border>(window, "ResultPane").IsVisible);
        Assert.Equal("composer.out.raw", Find<TextBlock>(window, "OutputText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MissingModels_ShowTheConsentBannerWithOneAction()
    {
        var (window, h) = Open(modelsInstalled: false);
        await using var _ = h;

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.True(Find<Border>(window, "BannerBox").IsVisible);
        Assert.Single(Find<StackPanel>(window, "BannerActions").Children);
        Assert.False(Find<ProgressBar>(window, "BannerProgress").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EscapeHidesTheComposer()
    {
        var (window, h) = Open();
        await using var _ = h;

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        await Wait.Until(() => !window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Enter_WritesTheResult()
    {
        var (window, h) = Open();
        await using var _ = h;
        Find<TextBox>(window, "SourceBox").Text = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Find<TextBox>(window, "SourceBox").Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.Until(() => h.Events.Contains("completed"));

        await h.Injector.Received(1).InjectAsync(Harness.Target, "T:先检查缓存。", false, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ShiftEnter_InsertsALineBreak_AndWritesNothing()
    {
        var (window, h) = Open();
        await using var _ = h;
        Find<TextBox>(window, "SourceBox").Text = "先检查缓存。";
        Find<TextBox>(window, "SourceBox").Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);

        Assert.Contains("\n", h.Vm.SourceText);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task TheMicrophoneButton_ShowsTheRecordingStateAndTheLiveSource()
    {
        var (window, h) = Open(output: ComposerOutput.Original);
        await using var _ = h;
        var mic = Find<Button>(window, "MicButton");

        await h.Vm.StartListeningAsync();
        Assert.Contains("recording", mic.Classes);
        Assert.True(Find<Control>(window, "MicBars").IsVisible);
        Assert.False(Find<Control>(window, "MicIcon").IsVisible);
        Assert.True(Find<ItemsControl>(window, "SourceLive").IsVisible);
        Assert.False(Find<TextBox>(window, "SourceBox").IsVisible);
        Assert.Equal("composer.mic.stop", AutomationProperties.GetName(mic));

        await h.Vm.StopListeningAsync();
        Assert.DoesNotContain("recording", mic.Classes);
        Assert.True(Find<TextBox>(window, "SourceBox").IsVisible);
        Assert.Equal("composer.mic.start", AutomationProperties.GetName(mic));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheMicrophoneDeniedBanner_OffersTheSystemSettings()
    {
        var (window, h) = Open(output: ComposerOutput.Original);
        await using var _ = h;
        h.Permission.Status.Returns(MicrophoneAccess.Denied);

        await h.Vm.StartListeningAsync();
        var open = (Button)Find<StackPanel>(window, "BannerActions").Children[0];
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Wait.Until(() => h.Permission.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IMicrophonePermission.OpenSettings)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task EveryTextDisplay_IsSelectable_SoItCanBeCopied()
    {
        var (window, h) = Open(modelsInstalled: false);
        await using var _ = h;

        Assert.IsType<SelectableTextBlock>(Find<SelectableTextBlock>(window, "BannerText"));
        Assert.False(Find<TextBox>(window, "SourceBox").IsReadOnly);
        window.Close();
    }

    [AvaloniaFact]
    public async Task InteractiveControlsWithoutVisibleText_HaveAccessibleNames_AndStatusIsALiveRegion()
    {
        var (window, h) = Open();
        await using var _ = h;

        Assert.Equal("composer.mic.start", AutomationProperties.GetName(Find<Button>(window, "MicButton")));
        Assert.Equal("composer.more", AutomationProperties.GetName(Find<Button>(window, "MoreButton")));
        Assert.Equal("composer.close", AutomationProperties.GetName(Find<Button>(window, "CloseButton")));
        Assert.Equal("composer.copy.aria", AutomationProperties.GetName(Find<Button>(window, "CopyButton")));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "StatusText")));
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(Find<SelectableTextBlock>(window, "BannerText")));
        window.Close();
    }

    [AvaloniaFact]
    public async Task AnImportedRecording_WidensThePanel_PutsTheResultBesideTheSource_AndShowsTimes()
    {
        var (window, h) = Open(output: ComposerOutput.Clean);
        await using var _ = h;
        h.Transcriber.Handler = (_, _, _) => Task.FromResult<IReadOnlyList<LiveLingo.Core.Transcription.TimedSegment>>(
            [new(TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(70), "先看缓存。")]);
        Assert.Equal(620 + 36, window.Width);

        await h.Vm.ImportAsync("a.wav");
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        await Wait.Until(() => window.Width == 940 + 36);

        Assert.Equal(940, Find<Border>(window, "Panel").Width);
        Assert.Equal(1, Grid.GetColumn(Find<Border>(window, "ResultPane")));
        Assert.Equal(0, Grid.GetRow(Find<Border>(window, "ResultPane")));
        Assert.True(Find<ItemsControl>(window, "SourceLive").IsVisible);
        Assert.Equal("composer.src.transcript", Find<TextBlock>(window, "SourceLabel").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ReducedMotion_StopsTheOpeningAnimation()
    {
        var (window, h) = Open(options: ComposerOptions.Default with { Animations = false });
        await using var _ = h;

        Assert.Contains("still", window.Classes);
        Assert.Equal(1, Find<Border>(window, "Panel").Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheCommitKeyCap_FollowsTheSetting()
    {
        var (window, h) = Open(options: ComposerOptions.Default with { CommitKey = CommitKey.CtrlEnter });
        await using var _ = h;

        Assert.Equal(ComposerTexts.CommitKeyLabel(CommitKey.CtrlEnter), Find<TextBlock>(window, "HintCommitKey").Text);
        window.Close();
    }
}
