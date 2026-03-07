using LiveLingo.App.Services.Configuration;
using LiveLingo.App.ViewModels;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
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
    public async Task SaveCommand_CallsUpdate()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);
        vm.OverlayHotkey = "Ctrl+Z";

        await vm.SaveCommand.ExecuteAsync(null);

        svc.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public async Task SaveCommand_ClearsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        vm.OverlayHotkey = "Ctrl+Z";
        Assert.True(vm.IsDirty);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_RaisesRequestClose()
    {
        var vm = new SettingsViewModel(CreateSettings());
        bool closed = false;
        vm.RequestClose += () => closed = true;

        await vm.SaveCommand.ExecuteAsync(null);

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

    [Fact]
    public async Task SaveCommand_PersistsAllEditedValues()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);

        vm.OverlayHotkey = "Alt+X";
        vm.DefaultSourceLanguage = "ja";
        vm.DefaultTargetLanguage = "zh";
        vm.DefaultPostProcessMode = "Optimize";
        vm.DefaultInjectionMode = "PasteOnly";
        vm.OverlayOpacity = 0.7;
        vm.ModelStoragePath = @"D:\ai";
        vm.InferenceThreads = 8;
        vm.LogLevel = "Warning";

        UserSettings? saved = null;
        svc.When(s => s.Update(Arg.Any<Func<UserSettings, UserSettings>>()))
            .Do(c =>
            {
                var mutator = c.ArgAt<Func<UserSettings, UserSettings>>(0);
                saved = mutator(new UserSettings());
            });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("Alt+X", saved!.Hotkeys.OverlayToggle);
        Assert.Equal("ja", saved.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
        Assert.Equal("Optimize", saved.Processing.DefaultMode);
        Assert.Equal("PasteOnly", saved.UI.DefaultInjectionMode);
        Assert.Equal(0.7, saved.UI.OverlayOpacity);
        Assert.Equal(@"D:\ai", saved.Advanced.ModelStoragePath);
        Assert.Equal(8, saved.Advanced.InferenceThreads);
        Assert.Equal("Warning", saved.Advanced.LogLevel);
    }

    [Fact]
    public void ResetCommand_RestoresAllDefaults()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" },
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja", DefaultTargetLanguage = "zh" },
            Advanced = new AdvancedSettings { InferenceThreads = 8, LogLevel = "Debug" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings));

        vm.ResetCommand.Execute(null);

        var defaults = new UserSettings();
        Assert.Equal(defaults.Hotkeys.OverlayToggle, vm.OverlayHotkey);
        Assert.Equal(defaults.Translation.DefaultSourceLanguage, vm.DefaultSourceLanguage);
        Assert.Equal(defaults.Translation.DefaultTargetLanguage, vm.DefaultTargetLanguage);
        Assert.Equal(defaults.Advanced.InferenceThreads, vm.InferenceThreads);
        Assert.Equal(defaults.Advanced.LogLevel, vm.LogLevel);
    }

    [Fact]
    public void CancelCommand_RestoresAllOriginalValues()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" },
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja" },
            Advanced = new AdvancedSettings { InferenceThreads = 8 }
        };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc);

        vm.OverlayHotkey = "Ctrl+Z";
        vm.DefaultSourceLanguage = "ko";
        vm.InferenceThreads = 2;

        vm.CancelCommand.Execute(null);

        Assert.Equal("Alt+X", vm.OverlayHotkey);
        Assert.Equal("ja", vm.DefaultSourceLanguage);
        Assert.Equal(8, vm.InferenceThreads);
    }

    [Theory]
    [InlineData(nameof(SettingsViewModel.DefaultSourceLanguage))]
    [InlineData(nameof(SettingsViewModel.DefaultTargetLanguage))]
    [InlineData(nameof(SettingsViewModel.OverlayOpacity))]
    [InlineData(nameof(SettingsViewModel.InferenceThreads))]
    [InlineData(nameof(SettingsViewModel.LogLevel))]
    public void AnyPropertyChange_SetsDirty(string propertyName)
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.False(vm.IsDirty);

        switch (propertyName)
        {
            case nameof(SettingsViewModel.DefaultSourceLanguage): vm.DefaultSourceLanguage = "ja"; break;
            case nameof(SettingsViewModel.DefaultTargetLanguage): vm.DefaultTargetLanguage = "zh"; break;
            case nameof(SettingsViewModel.OverlayOpacity): vm.OverlayOpacity = 0.5; break;
            case nameof(SettingsViewModel.InferenceThreads): vm.InferenceThreads = 4; break;
            case nameof(SettingsViewModel.LogLevel): vm.LogLevel = "Debug"; break;
        }

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_MigratesModelPath_WhenChanged()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = @"C:\old" }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        vm.ModelStoragePath = @"D:\new";
        await vm.SaveCommand.ExecuteAsync(null);

        await modelManager.Received(1).MigrateStoragePathAsync(@"D:\new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_SkipsMigration_WhenPathUnchanged()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = @"C:\same" }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        await vm.SaveCommand.ExecuteAsync(null);

        await modelManager.DidNotReceive().MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_SkipsMigration_WhenPathFormatOnlyDiffers()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = @"C:\models" }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);
        vm.OverlayHotkey = "Ctrl+Alt+Y"; // make dirty
        vm.ModelStoragePath = @"C:\models\";

        await vm.SaveCommand.ExecuteAsync(null);

        await modelManager.DidNotReceive().MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WithModelManager_PopulatesModels()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns([]);

        var vm = new SettingsViewModel(CreateSettings(), modelManager);

        Assert.NotNull(vm.Models);
        Assert.Equal(ModelRegistry.AllModels.Count, vm.Models.Count);
    }

    [Fact]
    public async Task SaveCommand_SetsMigrationError_OnFailure()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = @"C:\old" }
        });
        var modelManager = Substitute.For<IModelManager>();
        modelManager.MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Access denied")));
        var vm = new SettingsViewModel(svc, modelManager);

        vm.ModelStoragePath = @"D:\new";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Access denied", vm.MigrationError);
        svc.DidNotReceive().Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public async Task SaveCommand_ClearsMigrationError_OnSuccess()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = @"C:\old" }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        vm.ModelStoragePath = @"D:\new";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.MigrationError);
    }

    [Fact]
    public void AvailableLanguages_ComesFromEngine()
    {
        var engine = new StubTranslationEngine();
        var vm = new SettingsViewModel(CreateSettings(), engine);

        Assert.Equal(engine.SupportedLanguages.Count, vm.AvailableLanguages.Count);
        Assert.Contains(vm.AvailableLanguages, l => l.Code == "en");
    }

    [Fact]
    public void AvailableLanguages_EmptyWithoutEngine()
    {
        var vm = new SettingsViewModel(CreateSettings());

        Assert.Empty(vm.AvailableLanguages);
    }

    [Fact]
    public void SelectedSourceLanguage_LoadedFromSettings()
    {
        var engine = new StubTranslationEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultSourceLanguage = "zh" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings), engine);

        Assert.NotNull(vm.SelectedSourceLanguage);
        Assert.Equal("zh", vm.SelectedSourceLanguage!.Code);
    }

    [Fact]
    public void SelectedTargetLanguage_LoadedFromSettings()
    {
        var engine = new StubTranslationEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings), engine);

        Assert.NotNull(vm.SelectedTargetLanguage);
        Assert.Equal("en", vm.SelectedTargetLanguage!.Code);
    }

    [Fact]
    public async Task SaveCommand_PersistsSelectedLanguageCodes()
    {
        var engine = new StubTranslationEngine();
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc, engine);

        vm.SelectedSourceLanguage = engine.SupportedLanguages.First(l => l.Code == "ja");
        vm.SelectedTargetLanguage = engine.SupportedLanguages.First(l => l.Code == "zh");

        UserSettings? saved = null;
        svc.When(s => s.Update(Arg.Any<Func<UserSettings, UserSettings>>()))
            .Do(c =>
            {
                var mutator = c.ArgAt<Func<UserSettings, UserSettings>>(0);
                saved = mutator(new UserSettings());
            });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("ja", saved!.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public void SelectedUILanguage_DefaultsToEnglish()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.Equal("en-US", vm.SelectedUILanguage.Code);
    }

    [Fact]
    public void SelectedUILanguage_LoadedFromSettings()
    {
        var settings = new UserSettings
        {
            UI = new UISettings { Language = "zh-CN" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings));
        Assert.Equal("zh-CN", vm.SelectedUILanguage.Code);
    }

    [Fact]
    public async Task SaveCommand_PersistsUILanguage()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);

        vm.SelectedUILanguage = SettingsViewModel.UILanguages.First(l => l.Code == "zh-CN");

        UserSettings? saved = null;
        svc.When(s => s.Update(Arg.Any<Func<UserSettings, UserSettings>>()))
            .Do(c =>
            {
                var mutator = c.ArgAt<Func<UserSettings, UserSettings>>(0);
                saved = mutator(new UserSettings());
            });

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("zh-CN", saved!.UI.Language);
    }

    [Fact]
    public void UILanguages_ContainsExpectedOptions()
    {
        Assert.Equal(2, SettingsViewModel.UILanguages.Count);
        Assert.Contains(SettingsViewModel.UILanguages, l => l.Code == "en-US");
        Assert.Contains(SettingsViewModel.UILanguages, l => l.Code == "zh-CN");
    }
}
