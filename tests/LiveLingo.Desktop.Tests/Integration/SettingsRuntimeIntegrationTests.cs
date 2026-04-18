using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Integration;

public class SettingsRuntimeIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsRuntimeIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingoSettingsRuntimeIntegration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveInSettingsViewModel_PropagatesToOverlayViaMessenger()
    {
        var messenger = new WeakReferenceMessenger();
        var settingsService = new JsonSettingsService(_settingsPath);
        await settingsService.LoadAsync(TestContext.Current.CancellationToken);

        var engine = new TestEngine();
        var settingsVm = new SettingsViewModel(settingsService, engine, messenger);
        var overlayVm = new OverlayViewModel(
            new TargetWindowInfo(1, 2, "test", "Test", 0, 0, 1000, 700),
            Substitute.For<ITranslationPipeline>(),
            Substitute.For<ITextInjector>(),
            engine,
            settingsService.Current,
            settingsService: settingsService,
            messenger: messenger);

        settingsVm.WorkingCopy.UI.DefaultInjectionMode = "PasteOnly";
        settingsVm.WorkingCopy.Translation.DefaultTargetLanguage = "ja";
        settingsVm.WorkingCopy.Translation.ActiveTranslationModelId = null;
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(InjectionMode.PasteOnly, overlayVm.Mode);
        Assert.Equal("ja", overlayVm.TargetLanguage);
        Assert.Equal("ja", settingsService.Current.Translation.DefaultTargetLanguage);
        Assert.Equal("PasteOnly", settingsService.Current.UI.DefaultInjectionMode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveInSettingsViewModel_PersistsToJsonAndCanBeReloaded()
    {
        var settingsService = new JsonSettingsService(_settingsPath);
        await settingsService.LoadAsync(TestContext.Current.CancellationToken);

        var settingsVm = new SettingsViewModel(settingsService, new TestEngine());
        settingsVm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Shift+Y";
        settingsVm.WorkingCopy.UI.Language = "zh-CN";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        var reloaded = new JsonSettingsService(_settingsPath);
        await reloaded.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ctrl+Shift+Y", reloaded.Current.Hotkeys.OverlayToggle);
        Assert.Equal("zh-CN", reloaded.Current.UI.Language);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SetupWizard_OpenAdvancedAndSaveToken_UpdatesWizardRecoveryState()
    {
        var messenger = new WeakReferenceMessenger();
        var settingsService = new JsonSettingsService(_settingsPath);
        var coreOptions = new CoreOptions();
        await settingsService.LoadAsync(TestContext.Current.CancellationToken);

        var settingsVm = new SettingsViewModel(
            settingsService,
            engine: new TestEngine(),
            messenger: messenger,
            coreOptions: coreOptions);
        var wizardVm = new SetupWizardViewModel(
            settingsService,
            messenger: messenger,
            coreOptions: coreOptions);
        var recipient = new object();

        messenger.Register<object, AppUiRequestMessage>(
            recipient,
            (_, message) =>
            {
                if (message.Value.Kind != AppUiRequestKind.OpenSettings) return;
                if (message.Value.SettingsInitialTabIndex is { } tab)
                    settingsVm.SelectedTabIndex = tab;
            });

        Assert.False(wizardVm.HasHuggingFaceTokenConfigured);
        Assert.True(wizardVm.ShowHuggingFaceTokenMissingCallout);
        Assert.Equal(0, settingsVm.SelectedTabIndex);

        wizardVm.OpenAdvancedForHuggingFaceCommand.Execute(null);

        Assert.Equal(3, settingsVm.SelectedTabIndex);

        settingsVm.WorkingCopy.Advanced.HuggingFaceToken = "hf_test_token";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("hf_test_token", settingsService.Current.Advanced.HuggingFaceToken);
        Assert.Equal("hf_test_token", coreOptions.HuggingFaceToken);
        Assert.True(wizardVm.HasHuggingFaceTokenConfigured);
        Assert.False(wizardVm.ShowHuggingFaceTokenMissingCallout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SyncCoreOptionsFromSettings_UpdatesLiveCoreOptionsFromPersistedSettings()
    {
        var services = new ServiceCollection();
        var coreOptions = new CoreOptions();
        var modelManager = Substitute.For<IModelManager>();
        services.AddSingleton(coreOptions);
        services.AddSingleton(modelManager);
        using var provider = services.BuildServiceProvider();

        var settings = SettingsModel.CreateDefault();
        settings.Translation.DefaultTargetLanguage = "ja";
        settings.Translation.ActiveTranslationModelId = ModelRegistry.Qwen25_7B.Id;
        settings.Translation.ModelPolicy.RoutingMode = nameof(TranslationRoutingMode.PreferCloud);
        settings.Translation.ModelPolicy.RouteUnsupportedPairsToCloud = true;
        settings.Translation.ModelPolicy.RoutePostProcessingToCloud = true;
        settings.Translation.CloudProvider.Enabled = true;
        settings.Translation.CloudProvider.BaseUrl = "https://api.openai.com/v1";
        settings.Translation.CloudProvider.ApiKey = "sk-test";
        settings.Translation.CloudProvider.TranslationModelId = "gpt-4.1-mini";
        settings.Translation.CloudProvider.PostProcessingModelId = "gpt-4.1-nano";
        settings.Advanced.HuggingFaceToken = "hf_test_token";
        settings.Advanced.InferenceThreads = 6;

        App.SyncCoreOptionsFromSettings(provider, settings);

        Assert.Equal("ja", coreOptions.DefaultTargetLanguage);
        Assert.Equal(ModelRegistry.Qwen25_7B.Id, coreOptions.ActiveTranslationModelId);
        Assert.Equal(TranslationRoutingMode.PreferCloud, coreOptions.TranslationRoutingMode);
        Assert.True(coreOptions.RouteUnsupportedLanguagePairsToCloud);
        Assert.True(coreOptions.RoutePostProcessingToCloud);
        Assert.True(coreOptions.CloudProviderEnabled);
        Assert.Equal("https://api.openai.com/v1", coreOptions.CloudProviderBaseUrl);
        Assert.Equal("sk-test", coreOptions.CloudProviderApiKey);
        Assert.Equal("gpt-4.1-mini", coreOptions.CloudTranslationModelId);
        Assert.Equal("gpt-4.1-nano", coreOptions.CloudPostProcessingModelId);
        Assert.Equal("hf_test_token", coreOptions.HuggingFaceToken);
        Assert.Equal(6, coreOptions.InferenceThreads);
        modelManager.Received(1).ResetHuggingfaceTransportFallback();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private sealed class TestEngine : ITranslationEngine
    {
        public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        [
            new("zh", "Chinese"),
            new("en", "English"),
            new("ja", "Japanese")
        ];

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
            => Task.FromResult(text);

        public async IAsyncEnumerable<LiveLingo.Core.Engines.TranslationDelta> TranslateStreamingAsync(
            string text, string sourceLanguage, string targetLanguage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new LiveLingo.Core.Engines.TranslationDelta(text);
        }

        public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) => true;

        public void Dispose()
        {
        }
    }
}
