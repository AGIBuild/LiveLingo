using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.ViewModels.Settings;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using NSubstitute;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class SettingsViewModelTests
{
    private ISettingsService CreateSettings(UserSettings? initial = null)
    {
        var state = initial ?? new UserSettings();
        var svc = Substitute.For<ISettingsService>();
        svc.Current.Returns(_ => state);
        svc.CloneCurrent().Returns(_ => state.DeepClone());
        svc.When(x => x.Replace(Arg.Any<UserSettings>()))
            .Do(ci => state = ci.Arg<UserSettings>().DeepClone());
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

        Assert.Equal("Alt+X", vm.WorkingCopy.Hotkeys.OverlayToggle);
        Assert.Equal("ja", vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void PropertyChange_SetsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.False(vm.IsDirty);

        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_CallsReplace()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";

        await vm.SaveCommand.ExecuteAsync(null);

        svc.Received(1).Replace(Arg.Any<UserSettings>());
    }

    [Fact]
    public async Task SaveCommand_ClearsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";
        Assert.True(vm.IsDirty);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_SendsCloseMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        var settingsChanged = false;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        messenger.Register<object, SettingsChangedMessage>(recipient, (_, _) =>
            settingsChanged = true);
        var vm = new SettingsViewModel(CreateSettings(), messenger: messenger);
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Shift+K";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(settingsChanged);
        Assert.Equal(AppUiRequestKind.CloseSettings, receivedKind);
    }

    [Fact]
    public async Task SaveCommand_WhenNotDirty_OnlyClosesWithoutPersistingOrBroadcasting()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        var settingsChanged = false;
        var svc = CreateSettings();
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        messenger.Register<object, SettingsChangedMessage>(recipient, (_, _) =>
            settingsChanged = true);
        var vm = new SettingsViewModel(svc, messenger: messenger);

        Assert.False(vm.IsDirty);

        await vm.SaveCommand.ExecuteAsync(null);

        svc.DidNotReceive().Replace(Arg.Any<UserSettings>());
        Assert.False(settingsChanged);
        Assert.Equal(AppUiRequestKind.CloseSettings, receivedKind);
    }

    [Fact]
    public void ResetCommand_RestoresDefaults()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings));
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";

        vm.ResetCommand.Execute(null);

        Assert.Equal("Ctrl+Alt+T", vm.WorkingCopy.Hotkeys.OverlayToggle);
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
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";

        vm.CancelCommand.Execute(null);

        Assert.Equal("Alt+X", vm.WorkingCopy.Hotkeys.OverlayToggle);
    }

    [Fact]
    public void CancelCommand_SendsCloseMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        var vm = new SettingsViewModel(CreateSettings(), messenger: messenger);

        vm.CancelCommand.Execute(null);

        Assert.Equal(AppUiRequestKind.CloseSettings, receivedKind);
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

        Assert.Equal("ja", vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", vm.WorkingCopy.Translation.DefaultTargetLanguage);
        Assert.Equal("Summarize", vm.WorkingCopy.Processing.DefaultMode);
        Assert.Equal("PasteOnly", vm.WorkingCopy.UI.DefaultInjectionMode);
        Assert.Equal(0.8, vm.WorkingCopy.UI.OverlayOpacity);
        Assert.Equal(@"C:\models", vm.WorkingCopy.Advanced.ModelStoragePath);
        Assert.Equal(4, vm.WorkingCopy.Advanced.InferenceThreads);
        Assert.Equal("Debug", vm.WorkingCopy.Advanced.LogLevel);
    }

    [Fact]
    public async Task SaveCommand_PersistsAllEditedValues()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);

        vm.WorkingCopy.Hotkeys.OverlayToggle = "Alt+X";
        vm.WorkingCopy.Translation.DefaultSourceLanguage = "ja";
        vm.WorkingCopy.Translation.DefaultTargetLanguage = "zh";
        vm.WorkingCopy.Processing.DefaultMode = "Optimize";
        vm.WorkingCopy.UI.DefaultInjectionMode = "PasteOnly";
        vm.WorkingCopy.UI.OverlayOpacity = 0.7;
        vm.WorkingCopy.Advanced.ModelStoragePath = @"D:\ai";
        vm.WorkingCopy.Advanced.InferenceThreads = 8;
        vm.WorkingCopy.Advanced.LogLevel = "Warning";

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = svc.Current;

        Assert.Equal("Alt+X", saved.Hotkeys.OverlayToggle);
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
    public void Constructor_LoadsHybridRoutingSettings()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                ModelPolicy = new ModelPolicySettings
                {
                    RoutingMode = nameof(TranslationRoutingMode.PreferCloud),
                    RouteUnsupportedPairsToCloud = true,
                    RoutePostProcessingToCloud = true,
                    PreferredLocalTranslationModelId = "opus-mt-zh-en"
                },
                CloudProvider = new CloudProviderSettings
                {
                    Enabled = true,
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKey = "sk-test",
                    TranslationModelId = "gpt-4.1-mini",
                    PostProcessingModelId = "gpt-4.1-nano"
                }
            }
        };

        var vm = new SettingsViewModel(CreateSettings(settings));

        Assert.Equal(nameof(TranslationRoutingMode.PreferCloud), vm.WorkingCopy.Translation.ModelPolicy.RoutingMode);
        Assert.True(vm.WorkingCopy.Translation.ModelPolicy.RouteUnsupportedPairsToCloud);
        Assert.True(vm.WorkingCopy.Translation.ModelPolicy.RoutePostProcessingToCloud);
        Assert.True(vm.WorkingCopy.Translation.CloudProvider.Enabled);
        Assert.Equal("https://api.openai.com/v1", vm.WorkingCopy.Translation.CloudProvider.BaseUrl);
        Assert.Equal("gpt-4.1-mini", vm.WorkingCopy.Translation.CloudProvider.TranslationModelId);
        Assert.Equal("gpt-4.1-nano", vm.WorkingCopy.Translation.CloudProvider.PostProcessingModelId);
    }

    [Fact]
    public async Task SaveCommand_PersistsHybridRoutingSettings()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);

        vm.WorkingCopy.Translation.ModelPolicy.RoutingMode = nameof(TranslationRoutingMode.PreferCloud);
        vm.WorkingCopy.Translation.ModelPolicy.RouteUnsupportedPairsToCloud = true;
        vm.WorkingCopy.Translation.ModelPolicy.RoutePostProcessingToCloud = true;
        vm.WorkingCopy.Translation.CloudProvider.Enabled = true;
        vm.WorkingCopy.Translation.CloudProvider.BaseUrl = "https://api.openai.com/v1";
        vm.WorkingCopy.Translation.CloudProvider.ApiKey = "sk-test";
        vm.WorkingCopy.Translation.CloudProvider.TranslationModelId = "gpt-4.1-mini";
        vm.WorkingCopy.Translation.CloudProvider.PostProcessingModelId = "gpt-4.1-nano";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(nameof(TranslationRoutingMode.PreferCloud), svc.Current.Translation.ModelPolicy.RoutingMode);
        Assert.True(svc.Current.Translation.ModelPolicy.RouteUnsupportedPairsToCloud);
        Assert.True(svc.Current.Translation.ModelPolicy.RoutePostProcessingToCloud);
        Assert.True(svc.Current.Translation.CloudProvider.Enabled);
        Assert.Equal("https://api.openai.com/v1", svc.Current.Translation.CloudProvider.BaseUrl);
        Assert.Equal("sk-test", svc.Current.Translation.CloudProvider.ApiKey);
        Assert.Equal("gpt-4.1-mini", svc.Current.Translation.CloudProvider.TranslationModelId);
        Assert.Equal("gpt-4.1-nano", svc.Current.Translation.CloudProvider.PostProcessingModelId);
    }

    [Fact]
    public void CloudProviderPresetChange_AppliesPresetDefaults()
    {
        var vm = new SettingsViewModel(CreateSettings());

        vm.WorkingCopy.Translation.CloudProvider.PresetId = CloudProviderPresetCatalog.OpenRouter.Id;

        Assert.Equal("https://openrouter.ai/api/v1", vm.WorkingCopy.Translation.CloudProvider.BaseUrl);
        Assert.Equal("openai/gpt-4.1-mini", vm.WorkingCopy.Translation.CloudProvider.TranslationModelId);
        Assert.Equal("openai/gpt-4.1-nano", vm.WorkingCopy.Translation.CloudProvider.PostProcessingModelId);
    }

    [Fact]
    public void CloudProviderBaseUrlChange_ToUnknownGateway_SetsPresetToCustom()
    {
        var vm = new SettingsViewModel(CreateSettings());
        vm.WorkingCopy.Translation.CloudProvider.PresetId = CloudProviderPresetCatalog.OpenAI.Id;

        vm.WorkingCopy.Translation.CloudProvider.BaseUrl = "https://llm.example.com/v1";

        Assert.Equal(CloudProviderPresetCatalog.Custom.Id, vm.WorkingCopy.Translation.CloudProvider.PresetId);
    }

    [Fact]
    public void Constructor_InfersPresetFromExistingBaseUrl()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                CloudProvider = new CloudProviderSettings
                {
                    BaseUrl = "https://api.groq.com/openai/v1",
                    TranslationModelId = "llama-3.3-70b-versatile"
                }
            }
        };

        var vm = new SettingsViewModel(CreateSettings(settings));

        Assert.Equal(CloudProviderPresetCatalog.Groq.Id, vm.WorkingCopy.Translation.CloudProvider.PresetId);
    }

    [Fact]
    public void Constructor_LoadsCachedCloudProviderRuntimeState()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                CloudProvider = new CloudProviderSettings
                {
                    Enabled = true,
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKey = "sk-test",
                    TranslationModelId = "gpt-4.1-mini",
                    PostProcessingModelId = "gpt-4.1"
                }
            }
        };
        var snapshot = new CloudProviderRuntimeSnapshot(
            CloudProviderConfigurationFingerprint.Create(new CloudModelPreferences(
                true,
                "https://api.openai.com/v1",
                "sk-test",
                "gpt-4.1-mini",
                "gpt-4.1")),
            CloudProviderRuntimeStatus.Healthy,
            CloudProviderValidationMode.ModelCatalog,
            "Connection succeeded. 2 models available.",
            DateTimeOffset.Parse("2026-04-15T10:00:00Z"),
            DateTimeOffset.Parse("2026-04-16T10:00:00Z"),
            [
                new CloudProviderModelInfo("gpt-4.1-mini", "openai"),
                new CloudProviderModelInfo("gpt-4.1", "openai")
            ]);
        var runtimeState = new TestCloudProviderRuntimeState(snapshot);
        var vm = new SettingsViewModel(
            CreateSettings(settings),
            engine: new StubTranslationEngine(),
            cloudProviderRuntimeState: runtimeState);

        Assert.Equal("Connection succeeded. 2 models available.", vm.CloudProviderStatusMessage);
        Assert.True(vm.HasDiscoveredCloudModels);
        Assert.Collection(
            vm.DiscoveredCloudModels,
            first => Assert.Equal("gpt-4.1-mini", first.Id),
            second => Assert.Equal("gpt-4.1", second.Id));
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
        Assert.Equal(defaults.Hotkeys.OverlayToggle, vm.WorkingCopy.Hotkeys.OverlayToggle);
        Assert.Equal(defaults.Translation.DefaultSourceLanguage, vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.Equal(defaults.Translation.DefaultTargetLanguage, vm.WorkingCopy.Translation.DefaultTargetLanguage);
        Assert.Equal(defaults.Advanced.InferenceThreads, vm.WorkingCopy.Advanced.InferenceThreads);
        Assert.Equal(defaults.Advanced.LogLevel, vm.WorkingCopy.Advanced.LogLevel);
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

        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Z";
        vm.WorkingCopy.Translation.DefaultSourceLanguage = "ko";
        vm.WorkingCopy.Advanced.InferenceThreads = 2;

        vm.CancelCommand.Execute(null);

        Assert.Equal("Alt+X", vm.WorkingCopy.Hotkeys.OverlayToggle);
        Assert.Equal("ja", vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.Equal(8, vm.WorkingCopy.Advanced.InferenceThreads);
    }

    [Fact]
    public void CancelCommand_AfterReset_RestoresCurrentSettingsAndClearsDirty()
    {
        var settings = new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" },
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja", DefaultTargetLanguage = "zh" }
        };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc);

        vm.ResetCommand.Execute(null);
        Assert.True(vm.IsDirty);

        vm.CancelCommand.Execute(null);

        Assert.Equal("Alt+X", vm.WorkingCopy.Hotkeys.OverlayToggle);
        Assert.Equal("ja", vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", vm.WorkingCopy.Translation.DefaultTargetLanguage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_AfterReset_PersistsDefaults()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "custom-models");
        var svc = CreateSettings(new UserSettings
        {
            Hotkeys = new HotkeySettings { OverlayToggle = "Alt+X" },
            Translation = new TranslationSettings { DefaultSourceLanguage = "ja", DefaultTargetLanguage = "zh" },
            Advanced = new AdvancedSettings { ModelStoragePath = customPath, InferenceThreads = 8 }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        vm.ResetCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var defaults = new UserSettings();
        Assert.Equal(defaults.Hotkeys.OverlayToggle, svc.Current.Hotkeys.OverlayToggle);
        Assert.Equal(defaults.Translation.DefaultSourceLanguage, svc.Current.Translation.DefaultSourceLanguage);
        Assert.Equal(defaults.Translation.DefaultTargetLanguage, svc.Current.Translation.DefaultTargetLanguage);
        Assert.Equal(defaults.Advanced.InferenceThreads, svc.Current.Advanced.InferenceThreads);
    }

    [Theory]
    [InlineData("DefaultSourceLanguage")]
    [InlineData("DefaultTargetLanguage")]
    [InlineData("OverlayOpacity")]
    [InlineData("InferenceThreads")]
    [InlineData("LogLevel")]
    public void AnyPropertyChange_SetsDirty(string propertyName)
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.False(vm.IsDirty);

        switch (propertyName)
        {
            case "DefaultSourceLanguage": vm.WorkingCopy.Translation.DefaultSourceLanguage = "ja"; break;
            case "DefaultTargetLanguage": vm.WorkingCopy.Translation.DefaultTargetLanguage = "zh"; break;
            case "OverlayOpacity": vm.WorkingCopy.UI.OverlayOpacity = 0.5; break;
            case "InferenceThreads": vm.WorkingCopy.Advanced.InferenceThreads = 4; break;
            case "LogLevel": vm.WorkingCopy.Advanced.LogLevel = "Debug"; break;
        }

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void CloudProviderPropertyChange_SetsDirty()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.False(vm.IsDirty);

        vm.WorkingCopy.Translation.CloudProvider.TranslationModelId = "gpt-4.1-mini";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task SaveCommand_MigratesModelPath_WhenChanged()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), "old-models");
        var newPath = Path.Combine(Path.GetTempPath(), "new-models");
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = oldPath }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        vm.WorkingCopy.Advanced.ModelStoragePath = newPath;
        await vm.SaveCommand.ExecuteAsync(null);

        await modelManager.Received(1).MigrateStoragePathAsync(newPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_SkipsMigration_WhenPathUnchanged()
    {
        var samePath = Path.Combine(Path.GetTempPath(), "same-models");
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = samePath }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        await vm.SaveCommand.ExecuteAsync(null);

        await modelManager.DidNotReceive().MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_SkipsMigration_WhenPathFormatOnlyDiffers()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "models");
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = basePath }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);
        vm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Alt+Y";
        vm.WorkingCopy.Advanced.ModelStoragePath = basePath + Path.DirectorySeparatorChar;

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
    public void Constructor_WithInstalledCustomTranslationModel_IncludesInActiveModelList()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns([
            new InstalledModel(
                "opus-mt-ko-en",
                "MarianMT Korean→English",
                "/tmp/models/opus-mt-ko-en",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);

        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());

        Assert.Contains(vm.AvailableTranslationModels, m => m.Id == "opus-mt-ko-en");
    }

    [Fact]
    public void RefreshTranslationModelsCommand_RefreshesInstalledTranslationModelList()
    {
        var installed = new List<InstalledModel>();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(_ => installed);

        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());
        Assert.DoesNotContain(vm.AvailableTranslationModels, m => m.Id == "opus-mt-pt-en");

        installed.Add(new InstalledModel(
            "opus-mt-pt-en",
            "MarianMT Portuguese→English",
            "/tmp/models/opus-mt-pt-en",
            100,
            ModelType.Translation,
            DateTime.UtcNow));
        vm.RefreshTranslationModelsCommand.Execute(null);

        Assert.Contains(vm.AvailableTranslationModels, m => m.Id == "opus-mt-pt-en");
    }

    [Fact]
    public void ActiveModelList_OnlyContainsInstalledModels()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                "opus-mt-zh-en",
                "MarianMT Chinese→English",
                "/tmp/models/opus-mt-zh-en",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());

        Assert.Single(vm.AvailableTranslationModels);
        Assert.Equal("opus-mt-zh-en", vm.AvailableTranslationModels[0].Id);
        Assert.DoesNotContain(vm.AvailableTranslationModels, m =>
            string.Equals(m.Id, "opus-mt-en-zh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActiveModelList_Empty_ShowsDownloadHint()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns([]);
        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());

        Assert.True(vm.ShowNoInstalledModelsHint);
    }

    [Fact]
    public void ActiveModelList_HintUpdatesAfterRefresh()
    {
        var installed = new List<InstalledModel>();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(_ => installed);
        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());
        Assert.True(vm.ShowNoInstalledModelsHint);

        installed.Add(new InstalledModel(
            "opus-mt-zh-en",
            "MarianMT Chinese→English",
            "/tmp/models/opus-mt-zh-en",
            100,
            ModelType.Translation,
            DateTime.UtcNow));
        vm.RefreshTranslationModelsCommand.Execute(null);

        Assert.False(vm.ShowNoInstalledModelsHint);
    }

    [Fact]
    public void OpenModelsTabCommand_SwitchesToModelsTab()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.OpenModelsTabCommand.Execute(null);

        Assert.Equal((int)SettingsTab.Models, vm.SelectedTabIndex);
    }

    [Fact]
    public void Constructor_ExcludesQwenFromActiveModelList()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                ModelRegistry.Qwen25_15B.Id,
                ModelRegistry.Qwen25_15B.DisplayName,
                "/tmp/models/qwen",
                ModelRegistry.Qwen25_15B.SizeBytes,
                ModelType.PostProcessing,
                DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(CreateSettings(), modelManager, new StubTranslationEngine());

        Assert.DoesNotContain(vm.AvailableTranslationModels, m =>
            string.Equals(m.Id, ModelRegistry.Qwen25_15B.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_WithQwenAsActiveTranslationModel_ClearsInvalidSelection()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                ModelRegistry.Qwen25_15B.Id,
                ModelRegistry.Qwen25_15B.DisplayName,
                "/tmp/models/qwen",
                ModelRegistry.Qwen25_15B.SizeBytes,
                ModelType.PostProcessing,
                DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(CreateSettings(new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en",
                ActiveTranslationModelId = ModelRegistry.Qwen25_15B.Id
            }
        }), modelManager, new StubTranslationEngine());

        Assert.Null(vm.WorkingCopy.Translation.ActiveTranslationModelId);
    }

    [Fact]
    public async Task SaveCommand_SetsMigrationError_OnFailure()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), "old-models");
        var newPath = Path.Combine(Path.GetTempPath(), "new-models");
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = oldPath }
        });
        var modelManager = Substitute.For<IModelManager>();
        modelManager.MigrateStoragePathAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Access denied")));
        var vm = new SettingsViewModel(svc, modelManager);

        vm.WorkingCopy.Advanced.ModelStoragePath = newPath;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains("Access denied", vm.MigrationError);
        svc.DidNotReceive().Replace(Arg.Any<UserSettings>());
    }

    [Fact]
    public async Task SaveCommand_ClearsMigrationError_OnSuccess()
    {
        var oldPath = Path.Combine(Path.GetTempPath(), "old-models");
        var newPath = Path.Combine(Path.GetTempPath(), "new-models");
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { ModelStoragePath = oldPath }
        });
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(svc, modelManager);

        vm.WorkingCopy.Advanced.ModelStoragePath = newPath;
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.MigrationError);
    }

    [Fact]
    public void AvailableLanguages_ComesFromFixedCatalog()
    {
        var engine = new StubTranslationEngine();
        var vm = new SettingsViewModel(CreateSettings(), engine);

        Assert.Equal(LanguageCatalog.DefaultLanguages.Count, vm.AvailableLanguages.Count);
        Assert.Equal(LanguageCatalog.DefaultLanguages.Select(l => l.Code), vm.AvailableLanguages.Select(l => l.Code));
    }

    [Fact]
    public void AvailableLanguages_StillAvailableWithoutEngine()
    {
        var vm = new SettingsViewModel(CreateSettings());

        Assert.Equal(LanguageCatalog.DefaultLanguages.Count, vm.AvailableLanguages.Count);
        Assert.Equal(LanguageCatalog.DefaultLanguages.Select(l => l.Code), vm.AvailableLanguages.Select(l => l.Code));
    }

    [Fact]
    public void AvailableLanguages_UsesInjectedCatalog()
    {
        var catalog = Substitute.For<ILanguageCatalog>();
        catalog.All.Returns(
        [
            new LanguageInfo("fr", "French"),
            new LanguageInfo("de", "German")
        ]);
        var vm = new SettingsViewModel(CreateSettings(), languageCatalog: catalog);

        Assert.Equal(["fr", "de"], vm.AvailableLanguages.Select(l => l.Code));
    }

    [Fact]
    public void SourceLanguage_LoadedFromSettings()
    {
        var engine = new StubTranslationEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultSourceLanguage = "zh" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings), engine);

        Assert.Equal("zh", vm.WorkingCopy.Translation.DefaultSourceLanguage);
    }

    [Fact]
    public void TargetLanguage_LoadedFromSettings()
    {
        var engine = new StubTranslationEngine();
        var settings = new UserSettings
        {
            Translation = new TranslationSettings { DefaultTargetLanguage = "en" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings), engine);

        Assert.Equal("en", vm.WorkingCopy.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public async Task SaveCommand_PersistsSelectedLanguageCodes()
    {
        var engine = new StubTranslationEngine();
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc, engine);

        vm.WorkingCopy.Translation.DefaultSourceLanguage = "ja";
        vm.WorkingCopy.Translation.DefaultTargetLanguage = "zh";

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = svc.Current;

        Assert.Equal("ja", saved.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public void Constructor_PrefersActiveTranslationModel_WhenConfigured()
    {
        var engine = new StubTranslationEngine();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                "opus-mt-en-zh",
                "MarianMT English→Chinese",
                "/tmp/models/opus-mt-en-zh",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en",
                ActiveTranslationModelId = "opus-mt-en-zh"
            }
        };

        var vm = new SettingsViewModel(CreateSettings(settings), modelManager, engine);

        Assert.Equal("opus-mt-en-zh", vm.WorkingCopy.Translation.ActiveTranslationModelId);
        Assert.Equal("en", vm.WorkingCopy.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", vm.WorkingCopy.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public async Task SaveCommand_PersistsSelectedTranslationModel()
    {
        var engine = new StubTranslationEngine();
        var svc = CreateSettings();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                "opus-mt-en-zh",
                "MarianMT English→Chinese",
                "/tmp/models/opus-mt-en-zh",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(svc, modelManager, engine);
        vm.WorkingCopy.Translation.ActiveTranslationModelId = "opus-mt-en-zh";

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = svc.Current;

        Assert.Equal("opus-mt-en-zh", saved.Translation.ActiveTranslationModelId);
        Assert.Equal("en", saved.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public async Task SaveCommand_WhenOnlyLanguagePairChanges_SelectsMatchingInstalledTranslationModel()
    {
        var svc = CreateSettings(new UserSettings
        {
            Translation = new TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en"
            }
        });
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                "opus-mt-zh-en",
                "MarianMT Chinese→English",
                "/tmp/models/opus-mt-zh-en",
                100,
                ModelType.Translation,
                DateTime.UtcNow),
            new InstalledModel(
                "opus-mt-en-zh",
                "MarianMT English→Chinese",
                "/tmp/models/opus-mt-en-zh",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(svc, modelManager, new StubTranslationEngine());

        vm.WorkingCopy.Translation.DefaultSourceLanguage = "en";
        vm.WorkingCopy.Translation.DefaultTargetLanguage = "zh";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("opus-mt-en-zh", svc.Current.Translation.ActiveTranslationModelId);
        Assert.Equal("en", svc.Current.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", svc.Current.Translation.DefaultTargetLanguage);
    }

    [Fact]
    public void SelectedUILanguage_DefaultsToEnglish()
    {
        var vm = new SettingsViewModel(CreateSettings());
        Assert.Equal("en-US", vm.WorkingCopy.UI.Language);
    }

    [Fact]
    public void SelectedUILanguage_LoadedFromSettings()
    {
        var settings = new UserSettings
        {
            UI = new UISettings { Language = "zh-CN" }
        };
        var vm = new SettingsViewModel(CreateSettings(settings));
        Assert.Equal("zh-CN", vm.WorkingCopy.UI.Language);
    }

    [Fact]
    public async Task SaveCommand_PersistsUILanguage()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);

        vm.WorkingCopy.UI.Language = "zh-CN";

        await vm.SaveCommand.ExecuteAsync(null);
        var saved = svc.Current;

        Assert.Equal("zh-CN", saved.UI.Language);
    }

    [Fact]
    public async Task SaveCommand_WhenAdvancedLlmSettingChanges_RequestsLlmRetry()
    {
        var svc = CreateSettings(new UserSettings
        {
            Advanced = new AdvancedSettings { HuggingFaceToken = "old-token" }
        });
        var coordinator = Substitute.For<ILlmModelLoadCoordinator>();
        var coreOptions = new CoreOptions();
        var vm = new SettingsViewModel(
            svc,
            engine: new StubTranslationEngine(),
            coreOptions: coreOptions,
            llmCoordinator: coordinator);

        vm.WorkingCopy.Advanced.HuggingFaceToken = "new-token";

        await vm.SaveCommand.ExecuteAsync(null);

        await coordinator.Received(1).RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
        Assert.Equal("new-token", coreOptions.HuggingFaceToken);
    }

    [Fact]
    public async Task SaveCommand_WhenTranslationModelChanges_RequestsLlmRetry()
    {
        var svc = CreateSettings(new UserSettings
        {
            Translation = new TranslationSettings { ActiveTranslationModelId = "opus-mt-zh-en" }
        });
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns(
        [
            new InstalledModel(
                "opus-mt-zh-en",
                "MarianMT Chinese→English",
                "/tmp/models/opus-mt-zh-en",
                100,
                ModelType.Translation,
                DateTime.UtcNow),
            new InstalledModel(
                "opus-mt-en-zh",
                "MarianMT English→Chinese",
                "/tmp/models/opus-mt-en-zh",
                100,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var coordinator = Substitute.For<ILlmModelLoadCoordinator>();
        var coreOptions = new CoreOptions();
        var vm = new SettingsViewModel(
            svc,
            modelManager,
            new StubTranslationEngine(),
            coreOptions: coreOptions,
            llmCoordinator: coordinator);

        vm.WorkingCopy.Translation.ActiveTranslationModelId = "opus-mt-en-zh";

        await vm.SaveCommand.ExecuteAsync(null);

        await coordinator.Received(1).RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
        Assert.Equal("opus-mt-en-zh", coreOptions.ActiveTranslationModelId);
    }

    [Fact]
    public async Task SaveCommand_WhenOnlyUiSettingChanges_DoesNotRequestLlmRetry()
    {
        var svc = CreateSettings();
        var coordinator = Substitute.For<ILlmModelLoadCoordinator>();
        var coreOptions = new CoreOptions();
        var vm = new SettingsViewModel(
            svc,
            engine: new StubTranslationEngine(),
            coreOptions: coreOptions,
            llmCoordinator: coordinator);

        vm.WorkingCopy.UI.Language = "zh-CN";

        await vm.SaveCommand.ExecuteAsync(null);

        await coordinator.DidNotReceive().RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
        Assert.Equal("zh-CN", svc.Current.UI.Language);
    }

    [Fact]
    public async Task TestCloudProviderConnectionCommand_UpdatesStatusMessage()
    {
        var runtimeState = Substitute.For<ICloudProviderRuntimeState>();
        runtimeState.RefreshAsync(Arg.Any<CloudModelPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new CloudProviderRuntimeSnapshot(
                CloudProviderConfigurationFingerprint.Create(new CloudModelPreferences(
                    true,
                    "https://api.openai.com/v1",
                    "sk-test",
                    null,
                    null)),
                CloudProviderRuntimeStatus.Healthy,
                CloudProviderValidationMode.ModelCatalog,
                "Connection succeeded. 3 models available.",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                [
                    new CloudProviderModelInfo("gpt-4.1-mini", "openai"),
                    new CloudProviderModelInfo("gpt-4.1", "openai"),
                    new CloudProviderModelInfo("gpt-4.1-nano", "openai")
                ]));
        var vm = new SettingsViewModel(
            CreateSettings(),
            engine: new StubTranslationEngine(),
            cloudProviderRuntimeState: runtimeState);
        vm.WorkingCopy.Translation.CloudProvider.Enabled = true;
        vm.WorkingCopy.Translation.CloudProvider.BaseUrl = "https://api.openai.com/v1";
        vm.WorkingCopy.Translation.CloudProvider.ApiKey = "sk-test";

        await vm.TestCloudProviderConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Connection succeeded. 3 models available.", vm.CloudProviderStatusMessage);
        await runtimeState.Received(1).RefreshAsync(
            Arg.Is<CloudModelPreferences>(preferences =>
                preferences.BaseUrl == "https://api.openai.com/v1" &&
                preferences.ApiKey == "sk-test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchCloudProviderModelsCommand_LoadsDiscoveredModels()
    {
        var runtimeState = Substitute.For<ICloudProviderRuntimeState>();
        runtimeState.RefreshAsync(Arg.Any<CloudModelPreferences>(), Arg.Any<CancellationToken>())
            .Returns(new CloudProviderRuntimeSnapshot(
                CloudProviderConfigurationFingerprint.Create(new CloudModelPreferences(
                    true,
                    "https://api.openai.com/v1",
                    "sk-test",
                    null,
                    null)),
                CloudProviderRuntimeStatus.Healthy,
                CloudProviderValidationMode.ModelCatalog,
                "Loaded 2 models.",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                [
                    new CloudProviderModelInfo("gpt-4.1-mini", "openai"),
                    new CloudProviderModelInfo("gemini-2.5-flash", "openrouter")
                ]));
        var vm = new SettingsViewModel(
            CreateSettings(),
            engine: new StubTranslationEngine(),
            cloudProviderRuntimeState: runtimeState);
        vm.WorkingCopy.Translation.CloudProvider.Enabled = true;
        vm.WorkingCopy.Translation.CloudProvider.BaseUrl = "https://api.openai.com/v1";
        vm.WorkingCopy.Translation.CloudProvider.ApiKey = "sk-test";

        await vm.FetchCloudProviderModelsCommand.ExecuteAsync(null);

        Assert.True(vm.HasDiscoveredCloudModels);
        Assert.Collection(
            vm.DiscoveredCloudModels,
            first => Assert.Equal("gpt-4.1-mini", first.Id),
            second => Assert.Equal("gemini-2.5-flash", second.Id));
        Assert.Contains("2", vm.CloudProviderStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenDirectProbeValidated_ShowsCapabilityAwareStatusMessage()
    {
        var settings = new UserSettings
        {
            Translation = new TranslationSettings
            {
                ModelPolicy = new ModelPolicySettings
                {
                    RoutePostProcessingToCloud = true
                },
                CloudProvider = new CloudProviderSettings
                {
                    Enabled = true,
                    BaseUrl = "https://gateway.example.com/v1",
                    ApiKey = "sk-test",
                    TranslationModelId = "gpt-4.1-mini",
                    PostProcessingModelId = "gpt-4.1"
                }
            }
        };
        var snapshot = new CloudProviderRuntimeSnapshot(
            CloudProviderConfigurationFingerprint.Create(CoreOptionsSync.CreateCloudModelPreferences(settings)),
            CloudProviderRuntimeStatus.Healthy,
            CloudProviderValidationMode.DirectModelProbe,
            "Connection succeeded.",
            DateTimeOffset.Parse("2026-04-15T10:00:00Z"),
            DateTimeOffset.Parse("2026-04-16T10:00:00Z"),
            []);
        var runtimeState = new TestCloudProviderRuntimeState(snapshot);
        var vm = new SettingsViewModel(
            CreateSettings(settings),
            engine: new StubTranslationEngine(),
            localizationService: new LocalizationService(),
            cloudProviderRuntimeState: runtimeState);

        Assert.Contains("does not expose a model list", vm.CloudProviderStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gpt-4.1-mini", vm.CloudProviderStatusMessage, StringComparison.Ordinal);
        Assert.Contains("gpt-4.1", vm.CloudProviderStatusMessage, StringComparison.Ordinal);
        Assert.Contains("prevalidated", vm.CloudProviderStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UseCloudTranslationModelCommand_AssignsSelectedModel()
    {
        var vm = new SettingsViewModel(CreateSettings(), engine: new StubTranslationEngine());

        vm.UseCloudTranslationModelCommand.Execute("gpt-4.1-mini");
        vm.UseCloudPostProcessingModelCommand.Execute("gpt-4.1");

        Assert.Equal("gpt-4.1-mini", vm.WorkingCopy.Translation.CloudProvider.TranslationModelId);
        Assert.Equal("gpt-4.1", vm.WorkingCopy.Translation.CloudProvider.PostProcessingModelId);
    }

    [Fact]
    public void UILanguages_ContainsExpectedOptions()
    {
        Assert.Equal(2, SettingsViewModel.UILanguages.Count);
        Assert.Contains(SettingsViewModel.UILanguages, l => l.Code == "en-US");
        Assert.Contains(SettingsViewModel.UILanguages, l => l.Code == "zh-CN");
    }

    [Fact]
    public void CheckPermissionsCommand_RaisesEvent()
    {
        var svc = CreateSettings();
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        var vm = new SettingsViewModel(svc, messenger: messenger);

        vm.CheckPermissionsCommand.Execute(null);

        Assert.Equal(AppUiRequestKind.ShowSettingsPermissionDialog, receivedKind);
    }

    [Fact]
    public void ShowPermissionSection_TrueOnMac()
    {
        var svc = CreateSettings();
        var vm = new SettingsViewModel(svc);
        Assert.Equal(OperatingSystem.IsMacOS(), vm.ShowPermissionSection);
    }

    [Fact]
    public void SelectedUILanguage_FallbackToEnglish_WhenUnknownLanguage()
    {
        var settings = new UserSettings
        {
            UI = new UISettings { Language = "fr-FR" }
        };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc);

        Assert.Equal("en-US", vm.WorkingCopy.UI.Language);
    }

    [Fact]
    public void OpenAdvancedTabForTokenCommand_SelectsAdvancedTab()
    {
        var vm = new SettingsViewModel(CreateSettings());

        vm.OpenAdvancedTabForTokenCommand.Execute(null);

        Assert.Equal((int)SettingsTab.Advanced, vm.SelectedTabIndex);
    }

    [Fact]
    public void SttRoutingModeOptions_ContainsAllSttRoutingModes()
    {
        var vm = new SettingsViewModel(CreateSettings());

        Assert.Equal(3, vm.SttRoutingModeOptions.Count);
        Assert.Contains(vm.SttRoutingModeOptions, o => o.Value == nameof(LiveLingo.Core.Speech.SttRoutingMode.AccuracyFirst));
        Assert.Contains(vm.SttRoutingModeOptions, o => o.Value == nameof(LiveLingo.Core.Speech.SttRoutingMode.StreamingFirst));
        Assert.Contains(vm.SttRoutingModeOptions, o => o.Value == nameof(LiveLingo.Core.Speech.SttRoutingMode.MultilingualFirst));
    }

    [Fact]
    public void ActiveSttModelDisplayName_ReflectsCohereTranscribe_ByDefault()
    {
        var vm = new SettingsViewModel(CreateSettings());

        Assert.Equal(ModelRegistry.SherpaCohereTranscribe14LangInt8.DisplayName, vm.ActiveSttModelDisplayName);
    }

    [Fact]
    public void ActiveSttModelChange_RaisesPropertyChanged_ForBoundLabels()
    {
        var vm = new SettingsViewModel(CreateSettings());
        var changed = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.WorkingCopy.Speech.RoutingMode = nameof(LiveLingo.Core.Speech.SttRoutingMode.StreamingFirst);

        Assert.Contains(nameof(SettingsViewModel.ActiveSttModelDisplayName), changed);
        Assert.Contains(nameof(SettingsViewModel.ActiveSttModelSizeText), changed);
        Assert.Contains(nameof(SettingsViewModel.IsActiveSttModelInstalled), changed);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ActiveSttModelDisplayName_SwitchesToSenseVoice_WhenRoutingChangesToMultilingualFirst()
    {
        // Verifies P2.d wiring end-to-end: changing the routing mode in the working copy
        // must flow through SpeechModelRouting and land on the SenseVoice descriptor.
        var vm = new SettingsViewModel(CreateSettings());

        vm.WorkingCopy.Speech.RoutingMode = nameof(LiveLingo.Core.Speech.SttRoutingMode.MultilingualFirst);

        Assert.Equal(ModelRegistry.SherpaSenseVoiceSmallInt8.DisplayName, vm.ActiveSttModelDisplayName);
    }

    [Fact]
    public void IsActiveSttModelInstalled_ReturnsTrue_WhenModelManagerListsActiveModel()
    {
        var modelManager = Substitute.For<IModelManager>();
        var stt = ModelRegistry.SherpaCohereTranscribe14LangInt8;
        modelManager.ListInstalled().Returns([
            new InstalledModel(stt.Id, stt.DisplayName, "/tmp/x", stt.SizeBytes, ModelType.SpeechToText, DateTime.UtcNow)
        ]);
        var vm = new SettingsViewModel(CreateSettings(), modelManager);

        Assert.True(vm.IsActiveSttModelInstalled);
    }

    [Fact]
    public void IsActiveSttModelInstalled_ReturnsFalse_WhenModelManagerHasNoMatch()
    {
        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns([]);
        var vm = new SettingsViewModel(CreateSettings(), modelManager);

        Assert.False(vm.IsActiveSttModelInstalled);
    }

    [Fact]
    public void OpenHuggingFaceTokenSettingsPageCommand_OpensTokenUrl()
    {
        var platform = Substitute.For<IPlatformServices>();
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(CreateSettings(), modelManager, platformServices: platform);

        vm.OpenHuggingFaceTokenSettingsPageCommand.Execute(null);

        platform.Received(1).OpenUrl("https://huggingface.co/settings/tokens");
    }

    [Fact]
    public void OpenPrimaryTranslationModelOnHuggingFaceCommand_OpensModelPage()
    {
        var platform = Substitute.For<IPlatformServices>();
        var modelManager = Substitute.For<IModelManager>();
        var vm = new SettingsViewModel(CreateSettings(), modelManager, platformServices: platform);

        vm.OpenPrimaryTranslationModelOnHuggingFaceCommand.Execute(null);

        platform.Received(1).OpenUrl(Arg.Is<string>(url => url.Contains("huggingface.co", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LocalModelStatusMessage_IsNull_WhenUnloaded()
    {
        var localState = new FakeLocalModelRuntimeState(ModelLoadState.Unloaded, null);
        var vm = new SettingsViewModel(CreateSettings(), localModelRuntimeState: localState);

        Assert.Null(vm.LocalModelStatusMessage);
    }

    [Fact]
    public void LocalModelStatusMessage_ShowsModelName_WhenLoaded()
    {
        var descriptor = ModelRegistry.Gemma4_26B_A4B;
        var localState = new FakeLocalModelRuntimeState(ModelLoadState.Loaded, descriptor);
        var vm = new SettingsViewModel(CreateSettings(), localModelRuntimeState: localState);

        Assert.NotNull(vm.LocalModelStatusMessage);
        Assert.Contains(descriptor.DisplayName, vm.LocalModelStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalModelStatusMessage_Updates_WhenStateChanges()
    {
        var descriptor = ModelRegistry.Gemma4_26B_A4B;
        var localState = new FakeLocalModelRuntimeState(ModelLoadState.Unloaded, null);
        var vm = new SettingsViewModel(CreateSettings(), localModelRuntimeState: localState);

        Assert.Null(vm.LocalModelStatusMessage);

        localState.SimulateStateChange(ModelLoadState.Loading, descriptor);
        Assert.NotNull(vm.LocalModelStatusMessage);
        Assert.Contains(descriptor.DisplayName, vm.LocalModelStatusMessage, StringComparison.Ordinal);

        localState.SimulateStateChange(ModelLoadState.Loaded, descriptor);
        Assert.NotNull(vm.LocalModelStatusMessage);
        Assert.Contains(descriptor.DisplayName, vm.LocalModelStatusMessage, StringComparison.Ordinal);

        localState.SimulateStateChange(ModelLoadState.Unloaded, null);
        Assert.Null(vm.LocalModelStatusMessage);
    }

    private sealed class FakeLocalModelRuntimeState(ModelLoadState initialState, ModelDescriptor? initialDescriptor)
        : ILocalModelRuntimeState
    {
        private ModelLoadState _state = initialState;
        private ModelDescriptor? _descriptor = initialDescriptor;

        public ModelLoadState State => _state;
        public ModelDescriptor? ActiveModelDescriptor => _descriptor;
        public event Action<ModelLoadState>? StateChanged;

        public void SimulateStateChange(ModelLoadState newState, ModelDescriptor? descriptor)
        {
            _state = newState;
            _descriptor = descriptor;
            StateChanged?.Invoke(newState);
        }
    }

    private sealed class TestCloudProviderRuntimeState(CloudProviderRuntimeSnapshot snapshot) : ICloudProviderRuntimeState
    {
        public CloudProviderRuntimeSnapshot Current => snapshot;
        public event Action<CloudProviderRuntimeSnapshot>? Changed
        {
            add { }
            remove { }
        }
        public CloudProviderRoutingState GetRoutingState(CloudModelPreferences? preferences) =>
            new(false, false, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        public Task<CloudProviderRuntimeSnapshot> RefreshAsync(CloudModelPreferences? preferences, CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }

    [Fact]
    public async Task TestOllamaProviderConnection_PopulatesDiscoveredModelsOnSuccess()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        probe.TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaConnectionResult(true, "ok", 2));
        probe.GetModelCatalogAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaModelCatalogResult(
            [
                new OllamaModelInfo("gemma3:4b", 4_200_000_000, null, null),
                new OllamaModelInfo("qwen3:4b", 3_000_000_000, null, null)
            ]));
        var settings = new UserSettings();
        settings.Translation.OllamaProvider.Enabled = true;
        settings.Translation.OllamaProvider.BaseUrl = "http://localhost:11434";
        var vm = new SettingsViewModel(CreateSettings(settings), ollamaProbeService: probe);

        await vm.TestOllamaProviderConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsTestingOllamaProvider);
        Assert.Equal("ok", vm.OllamaProviderStatusMessage);
        Assert.True(vm.HasDiscoveredOllamaModels);
        Assert.Equal(2, vm.DiscoveredOllamaModels.Count);
        Assert.Equal("gemma3:4b", vm.DiscoveredOllamaModels[0].Id);
    }

    [Fact]
    public async Task TestOllamaProviderConnection_ClearsModelsOnFailure()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        probe.TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaConnectionResult(false, "Connection refused"));
        var settings = new UserSettings();
        settings.Translation.OllamaProvider.BaseUrl = "http://localhost:11434";
        var vm = new SettingsViewModel(CreateSettings(settings), ollamaProbeService: probe);

        await vm.TestOllamaProviderConnectionCommand.ExecuteAsync(null);

        Assert.Equal("Connection refused", vm.OllamaProviderStatusMessage);
        Assert.False(vm.HasDiscoveredOllamaModels);
        await probe.DidNotReceive().GetModelCatalogAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestOllamaProviderConnection_RejectsEmptyBaseUrl()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        var settings = new UserSettings();
        settings.Translation.OllamaProvider.BaseUrl = "   ";
        var vm = new SettingsViewModel(CreateSettings(settings), ollamaProbeService: probe);

        await vm.TestOllamaProviderConnectionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.OllamaProviderStatusMessage);
        Assert.Contains("required", vm.OllamaProviderStatusMessage, StringComparison.OrdinalIgnoreCase);
        await probe.DidNotReceive().TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UseOllamaTranslationModel_UpdatesWorkingCopy()
    {
        var vm = new SettingsViewModel(CreateSettings());

        vm.UseOllamaTranslationModelCommand.Execute("gemma3:4b");

        Assert.Equal("gemma3:4b", vm.WorkingCopy.Translation.OllamaProvider.TranslationModelId);
    }

    [Fact]
    public void UseOllamaPostProcessingModel_UpdatesWorkingCopy()
    {
        var vm = new SettingsViewModel(CreateSettings());

        vm.UseOllamaPostProcessingModelCommand.Execute("qwen3:4b");

        Assert.Equal("qwen3:4b", vm.WorkingCopy.Translation.OllamaProvider.PostProcessingModelId);
    }

    [Fact]
    public async Task OllamaBaseUrlChange_ClearsDiscoveredModels()
    {
        var probe = Substitute.For<IOllamaProbeService>();
        probe.TestConnectionAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaConnectionResult(true, "ok", 1));
        probe.GetModelCatalogAsync(Arg.Any<OllamaProbeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OllamaModelCatalogResult([new OllamaModelInfo("gemma3:4b", 1, null, null)]));
        var settings = new UserSettings();
        settings.Translation.OllamaProvider.BaseUrl = "http://localhost:11434";
        var vm = new SettingsViewModel(CreateSettings(settings), ollamaProbeService: probe);
        await vm.TestOllamaProviderConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.HasDiscoveredOllamaModels);

        vm.WorkingCopy.Translation.OllamaProvider.BaseUrl = "http://remote:11434";

        Assert.False(vm.HasDiscoveredOllamaModels);
        Assert.Null(vm.OllamaProviderStatusMessage);
    }
}
