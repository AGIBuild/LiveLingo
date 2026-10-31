using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerWindowTests
{
    private static (ComposerWindow Window, Harness Harness) Open(bool modelsInstalled = true)
    {
        var h = new Harness(modelsInstalled, notifyOn: SynchronizationContext.Current);
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        loc.T(Arg.Any<string>(), Arg.Any<object[]>()).Returns(call => call.ArgAt<string>(0));
        var window = new ComposerWindow(h.Vm, new ComposerTexts(loc), Harness.Target, "Copy", "Write", "Original", "Type here", "Retry");
        window.Show();
        return (window, h);
    }

    private static T Find<T>(ComposerWindow window, string name) where T : Control => window.FindControl<T>(name)!;

    [AvaloniaFact]
    public async Task OpensWithTheTargetAndAnEmptyComposer()
    {
        var (window, h) = Open();
        await using var _ = h;

        Assert.Equal("composer.target.some", Find<TextBlock>(window, "TargetText").Text);
        Assert.False(Find<Border>(window, "ResultBox").IsVisible);
        Assert.False(Find<Border>(window, "BannerBox").IsVisible);
        Assert.False(Find<Button>(window, "WriteButton").IsEnabled);
        Assert.Equal("Type here", Find<TextBox>(window, "SourceBox").Watermark);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TypingShowsTheResult_AndEnablesWriting()
    {
        var (window, h) = Open();
        await using var _ = h;

        Find<TextBox>(window, "SourceBox").Text = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.True(Find<Border>(window, "ResultBox").IsVisible);
        Assert.True(Find<Button>(window, "WriteButton").IsEnabled);
        Assert.True(Find<Button>(window, "CopyButton").IsEnabled);
        Assert.Equal("composer.status.ready", Find<TextBlock>(window, "StatusText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MissingModels_ShowTheConsentBanner()
    {
        var (window, h) = Open(modelsInstalled: false);
        await using var _ = h;

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.True(Find<Border>(window, "BannerBox").IsVisible);
        Assert.True(Find<Button>(window, "BannerAction").IsVisible);
        Assert.False(Find<ProgressBar>(window, "BannerProgress").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OutputButtons_SwitchTheOutput_AndSwapChangesTheLanguage()
    {
        var (window, h) = Open();
        await using var _ = h;

        Find<Button>(window, "OriginalButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ComposerOutput.Original, h.Vm.Output);
        Assert.False(Find<Button>(window, "SwapButton").IsVisible);

        Find<Button>(window, "TranslateButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ComposerOutput.Translate, h.Vm.Output);
        Assert.True(Find<Button>(window, "SwapButton").IsVisible);

        Find<Button>(window, "SwapButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(TargetLanguage.Chinese, h.Vm.TargetLanguage);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EscapeClosesTheComposer_AndCompletionClosesIt()
    {
        var (window, h) = Open();
        await using var _ = h;

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);

        Assert.False(window.IsVisible);
    }
}
