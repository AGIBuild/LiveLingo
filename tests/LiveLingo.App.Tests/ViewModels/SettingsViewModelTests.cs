using LiveLingo.App.Services.Configuration;
using LiveLingo.App.ViewModels;
using NSubstitute;

namespace LiveLingo.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private ISettingsService CreateSettings(UserSettings? initial = null)
    {
        var svc = Substitute.For<ISettingsService>();
        svc.Current.Returns(initial ?? new UserSettings());
        return svc;
    }

    [Fact]
    public void Constructor_LoadsFromSettings()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" },
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja" }
        };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc);

        Assert.Equal("Alt+X", vm.OverlayHotkey);
        Assert.Equal("ja", vm.DefaultSourceLanguage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void PropertyChange_SetsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.False(vm.IsDirty);

        vm.OverlayHotkey = "Ctrl+Z";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void SaveCommand_CallsUpdate()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);
        vm.OverlayHotkey = "Ctrl+Z";

        vm.SaveCommand.Execute(null);

        svc.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void SaveCommand_ClearsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        vm.OverlayHotkey = "Ctrl+Z";
        Assert.True(vm.IsDirty);

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void SaveCommand_RaisesRequestClose()
    {
        var vm = new SettingsViewModel(CreateSettings());
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.SaveCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void ResetCommand_RestoresDefaults()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings));
        vm.OverlayHotkey = "Ctrl+Z";

        vm.ResetCommand.Execute(null);

        Assert.Equal("Ctrl+Alt+T", vm.OverlayHotkey);
    }

    [Fact]
    public void CancelCommand_RestoresOriginal()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" }
        };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc);
        vm.OverlayHotkey = "Ctrl+Z";

        vm.CancelCommand.Execute(null);

        Assert.Equal("Alt+X", vm.OverlayHotkey);
    }

    [Fact]
    public void CancelCommand_RaisesRequestClose()
    {
        var vm = new SettingsViewModel(CreateSettings());
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void AllProperties_LoadedFromSettings()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "zh"
            },
            Processing = new ProcessingSettings { DefaultMode = "Summarize" },
            UI = new UISettings
            {
                OverlayOpacity = 0.8,
                DefaultInjectionMode = "PasteOnly"
            },
            Advanced = new AdvancedSettings
            {
                ModelStoragePath = @"C:\models",
                InferenceThreads = 4,
                LogLevel = "Debug"
            }
        };

        var vm = new SettingsViewModel(CreateSettings(settings));

        Assert.Equal("ja", vm.DefaultSourceLanguage);
        Assert.Equal("zh", vm.DefaultTargetLanguage);
        Assert.Equal("Summarize", vm.DefaultPostProcessMode);
        Assert.Equal("PasteOnly", vm.DefaultInjectionMode);
        Assert.Equal(0.8, vm.OverlayOpacity);
        Assert.Equal(@"C:\models", vm.ModelStoragePath);
        Assert.Equal(4, vm.InferenceThreads);
        Assert.Equal("Debug", vm.LogLevel);
    }
}
