using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Tests.Composer;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Settings;

public class SettingsWindowTests
{
    private static SettingsWindow Open(SettingsModel? model = null, string? storedKey = null, string section = "general")
    {
        var current = model ?? new SettingsModel();
        var service = Substitute.For<ISettingsService>();
        service.CloneCurrent().Returns(_ => current.DeepClone());
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>()).Returns(storedKey);
        var microphone = Substitute.For<IMicrophone>();
        microphone.InputDevices().Returns(["USB Mic"]);
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => call.Arg<string>());
        loc.T(Arg.Any<string>(), Arg.Any<object[]>()).Returns(call => call.ArgAt<string>(0));
        var vm = new SettingsViewModel(service, secrets, Substitute.For<IModelManager>(), new RecentDrafts(), microphone);
        var window = new SettingsWindow(vm, loc, section);
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void TheNavigation_ShowsOneSectionAtATime()
    {
        var window = Open();
        Assert.True(window.FindControl<StackPanel>("PanelGeneral")!.IsVisible);
        Assert.False(window.FindControl<StackPanel>("PanelPrivacy")!.IsVisible);

        window.ShowSection("privacy");

        Assert.False(window.FindControl<StackPanel>("PanelGeneral")!.IsVisible);
        Assert.True(window.FindControl<StackPanel>("PanelPrivacy")!.IsVisible);
        Assert.Contains("active", window.FindControl<Button>("NavPrivacy")!.Classes);
    }

    [AvaloniaFact]
    public void OpeningASection_ShowsItDirectly()
    {
        var window = Open(section: "models");

        Assert.True(window.FindControl<StackPanel>("PanelModels")!.IsVisible);
        Assert.False(window.FindControl<StackPanel>("PanelGeneral")!.IsVisible);
    }

    [AvaloniaFact]
    public void TheComboBoxes_StartOnTheCurrentSettings()
    {
        var model = new SettingsModel();
        model.Appearance.Theme = "Dark";
        model.Composer.CommitKey = "CtrlEnter";
        var window = Open(model);

        Assert.Equal(2, window.FindControl<ComboBox>("ThemeBox")!.SelectedIndex);
        Assert.Equal(1, window.FindControl<ComboBox>("CommitBox")!.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task CloudKeyBoxIsMasked_AndTheForgetButtonShowsOnlyWhenAKeyIsStored()
    {
        var window = Open(storedKey: "sk", section: "privacy");

        Assert.Equal('•', window.FindControl<TextBox>("CloudKeyBox")!.PasswordChar);
        Assert.Equal("settings.cloud.apiKey", AutomationProperties.GetName(window.FindControl<TextBox>("CloudKeyBox")!));
        await Wait.Until(() => window.FindControl<Button>("CloudForgetButton")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task TheMicrophoneList_ShowsTheSystemDefaultAndTheDevices()
    {
        var window = Open();

        await Wait.Until(() => window.FindControl<ComboBox>("MicBox")!.ItemCount == 2);

        Assert.Equal(0, window.FindControl<ComboBox>("MicBox")!.SelectedIndex);
    }
}
