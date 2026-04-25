using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.ViewModels.Settings;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Speech;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly ILocalModelRuntimeState _localModelRuntimeState;
    private readonly IPlatformServices? _platformServices;
    private readonly IMessenger _messenger;
    private readonly ILocalizationService? _loc;
    private readonly ILanguageCatalog _languageCatalog;
    private readonly ISettingsLocalizationHelper _localization;
    private readonly IWorkingCopyNormalizer _workingCopyNormalizer;
    private readonly ITranslationModelInventory _translationModelInventory;
    private readonly ICloudProviderPresetCoordinator _cloudPresetCoordinator;
    private readonly IOllamaProviderProbeOrchestrator _ollamaProbeOrchestrator;
    private readonly ICloudProviderProbeOrchestrator _cloudProbeOrchestrator;
    private readonly ITranslationLanguagePairSyncer _languagePairSyncer;
    private readonly ISettingsDirtyGuard _dirtyGuard;
    private readonly ISettingsPersistenceCoordinator _persistenceCoordinator;
    private string? _originalModelStoragePath;

    [ObservableProperty] private SettingsModel _workingCopy = SettingsModel.CreateDefault();
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _migrationError;
    [ObservableProperty] private bool _showPermissionSection;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _isTestingCloudProvider;
    [ObservableProperty] private bool _isFetchingCloudModels;
    [ObservableProperty] private string? _cloudProviderStatusMessage;
    [ObservableProperty] private bool _isTestingOllamaProvider;
    [ObservableProperty] private string? _ollamaProviderStatusMessage;
    [ObservableProperty] private string? _localModelStatusMessage;

    public static IReadOnlyList<string> InjectionModes { get; } = ["PasteAndSend", "PasteOnly"];
    public static IReadOnlyList<string> PostProcessModes { get; } = ["Off", "Summarize", "Optimize", "Colloquialize"];
    public static IReadOnlyList<string> LogLevels { get; } = ["Verbose", "Debug", "Information", "Warning", "Error"];
    public IReadOnlyList<SelectableOption> InjectionModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> PostProcessModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> LogLevelOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> RoutingModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> SttRoutingModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> CloudProviderPresetOptions { get; private set; } = [];
    public static IReadOnlyList<UILanguageOption> UILanguages { get; } =
        [new("en-US", "English"), new("zh-CN", "简体中文")];
    public string GeneralSectionHotkeys => L("settings.general.hotkeys", "Hotkeys");
    public string GeneralOverlayToggleLabel => L("settings.general.overlayToggle", "Overlay Toggle:");
    public string GeneralHotkeyHint => L("settings.general.hotkeyHint", "Click the field, then press a key combo");
    public string GeneralCheckPermissions => L("settings.general.checkPermissions", "Check Permissions…");
    public string GeneralSectionOverlay => L("settings.general.overlay", "Overlay");
    public string GeneralOpacityLabel => L("settings.general.opacity", "Opacity:");
    public string GeneralInjectionModeLabel => L("settings.general.injectionMode", "Injection Mode:");
    public string GeneralSectionLanguage => L("settings.general.language", "Language");
    public string GeneralUiLanguageLabel => L("settings.general.uiLanguage", "UI Language:");
    public string TranslationSectionDefaultPair => L("settings.translation.defaultPair", "Default Language Pair");
    public string TranslationSectionRouting => L("settings.translation.routing", "Routing Policy");
    public string TranslationSourceLabel => L("settings.translation.source", "Source Language:");
    public string TranslationTargetLabel => L("settings.translation.target", "Target Language:");
    public string TranslationActiveModelLabel => L("settings.translation.activeModel", "Preferred Local Model:");
    public string TranslationRoutingModeLabel => L("settings.translation.routingMode", "Routing Mode:");
    public string TranslationRouteUnsupportedPairsLabel => L(
        "settings.translation.routeUnsupportedPairs",
        "Route unsupported language pairs to cloud");
    public string TranslationRefreshModelsTooltip => L("settings.translation.refreshModels", "Refresh translation models");
    public string TranslationNoInstalledModelsHint => L(
        "settings.translation.noInstalledModelsHint",
        "No downloaded model available. Go to Models tab to download.");
    public string TranslationOpenModelsTab => L("settings.translation.openModelsTab", "Go to Models");
    public bool ShowNoInstalledModelsHint => AvailableTranslationModels.Count == 0;

    public string SpeechSectionRouting => L("settings.speech.routing", "Speech-to-Text Routing");
    public string SpeechRoutingModeLabel => L("settings.speech.routingMode", "Routing Mode:");
    public string SpeechRoutingModeHint => L(
        "settings.speech.routingModeHint",
        "All routing modes currently resolve to Cohere Transcribe (top-of-leaderboard accuracy). Streaming-first and Multilingual-first will pick distinct engines once additional bundles ship.");
    public string SpeechSectionActiveModel => L("settings.speech.activeModel", "Active STT Model");
    public string SpeechActiveModelLabel => L("settings.speech.modelName", "Model:");
    public string SpeechActiveModelSizeLabel => L("settings.speech.modelSize", "Size:");
    public string SpeechActiveModelStatusLabel => L("settings.speech.modelStatus", "Status:");
    public string SpeechActiveModelInstalledLabel => L("settings.speech.modelInstalled", "✓ Installed");
    public string SpeechActiveModelMissingLabel => L(
        "settings.speech.modelMissing",
        "Not downloaded — open the Models tab to download.");
    public string SpeechOpenModelsTabLabel => L("settings.speech.openModelsTab", "Go to Models");

    public string ActiveSttModelDisplayName
    {
        get
        {
            var descriptor = ResolveActiveSttModel();
            return descriptor.DisplayName;
        }
    }

    public string ActiveSttModelSizeText
    {
        get
        {
            var descriptor = ResolveActiveSttModel();
            return ModelItemViewModel.FormatBytes(descriptor.SizeBytes);
        }
    }

    public bool IsActiveSttModelInstalled
    {
        get
        {
            var descriptor = ResolveActiveSttModel();
            if (_modelManager is null)
                return false;
            return _modelManager.ListInstalled()
                .Any(m => string.Equals(m.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string ModelsDownloadLabel => L("settings.models.download", "Download");
    public string ModelsCancelLabel => L("settings.models.cancel", "Cancel");
    public string ModelsInstalledLabel => L("settings.models.installed", "✓ Installed");
    public string ModelsDeleteLabel => L("settings.models.delete", "Delete");
    public string ModelsHuggingFaceHint => L(
        "settings.models.huggingFaceHint",
        "Hugging Face downloads use the read access token under Advanced (huggingface.co/settings/tokens). After changing the token, click Save, then retry download here.");
    public string ModelsOpenAdvancedForTokenLabel => L("settings.models.openAdvancedForToken", "Open Advanced (token)…");
    public string AdvancedSectionModelStorage => L("settings.advanced.modelStorage", "Model Storage");
    public string AdvancedStoragePathLabel => L("settings.advanced.storagePath", "Storage Path:");
    public string AdvancedStoragePathPlaceholder => L("settings.advanced.defaultStoragePath", "Default (AppData)");
    public string AdvancedBrowseLabel => L("settings.advanced.browse", "Browse…");
    public string AdvancedSectionPerformance => L("settings.advanced.performance", "Performance");
    public string AdvancedInferenceThreadsLabel => L("settings.advanced.inferenceThreads", "Inference Threads:");
    public string AdvancedThreadsHint => L("settings.advanced.threadsHint", "0 = auto-detect (recommended)");
    public string AdvancedSectionHuggingFace => L("settings.advanced.huggingFace", "Hugging Face");
    public string AdvancedHuggingFaceMirrorLabel => L("settings.advanced.huggingFaceMirror", "Mirror base URL:");
    public string AdvancedHuggingFaceMirrorPlaceholder =>
        L("settings.advanced.huggingFaceMirrorPlaceholder", "https://hf-mirror.com (optional)");
    public string AdvancedHuggingFaceTokenLabel => L("settings.advanced.huggingFaceToken", "Access token:");
    public string AdvancedHuggingFaceTokenHint => L(
        "settings.advanced.huggingFaceTokenHint",
        "Strongly recommended for Qwen and other gated GGUF weights. Create a read token at huggingface.co/settings/tokens, paste it here, then Save. Models tab downloads use this token.");
    public bool ShowAdvancedHuggingFaceBrowserLinks => _platformServices is not null;
    public string AdvancedOpenHuggingFaceTokensLabel => L("settings.advanced.openHfTokensPage", "Open Hugging Face token settings…");
    public string AdvancedOpenTranslationModelLabel => L(
        "settings.advanced.openTranslationModelPage",
        "Open translation model page (accept access)…");
    public string AdvancedSectionLogging => L("settings.advanced.logging", "Logging");
    public string AdvancedLogLevelLabel => L("settings.advanced.logLevel", "Log Level:");
    public string AiSectionPostProcessing => L("settings.ai.postProcessing", "Post-Processing");
    public string AiDefaultModeLabel => L("settings.ai.defaultMode", "Default Mode:");
    public string AiModesHint => L("settings.ai.modesHint", "Summarize · Optimize · Colloquialize");
    public string AiSectionCloudProvider => L("settings.ai.cloudProvider", "Cloud Provider");
    public string AiCloudEnabledLabel => L("settings.ai.cloudEnabled", "Enable Cloud Provider");
    public string AiCloudPresetLabel => L("settings.ai.cloudPreset", "Preset:");
    public string AiCloudProviderLabel => L("settings.ai.cloudProviderType", "Provider:");
    public string AiCloudProviderValue => L("settings.ai.cloudProviderValue", "OpenAI-compatible Chat API");
    public string AiCloudBaseUrlLabel => L("settings.ai.cloudBaseUrl", "Base URL:");
    public string AiCloudBaseUrlPlaceholder => GetSelectedCloudProviderPreset().BaseUrl;
    public string AiCloudApiKeyLabel => L("settings.ai.cloudApiKey", "API Key:");
    public string AiCloudTranslationModelLabel => L("settings.ai.cloudTranslationModel", "Translation Model:");
    public string AiCloudTranslationModelPlaceholder => GetSelectedCloudProviderPreset().TranslationModelPlaceholder;
    public string AiCloudPostProcessingModelLabel => L("settings.ai.cloudPostModel", "Post-Processing Model:");
    public string AiCloudPostProcessingModelPlaceholder => GetSelectedCloudProviderPreset().PostProcessingModelPlaceholder;
    public string AiCloudTestConnectionLabel => L("settings.ai.cloudTestConnection", "Test Connection");
    public string AiCloudFetchModelsLabel => L("settings.ai.cloudFetchModels", "Fetch Models");
    public string AiCloudDiscoveredModelsLabel => L("settings.ai.cloudDiscoveredModels", "Discovered Models");
    public string AiCloudUseTranslationLabel => L("settings.ai.cloudUseTranslation", "Use for Translation");
    public string AiCloudUsePostProcessingLabel => L("settings.ai.cloudUsePost", "Use for Post-Processing");
    public string AiCloudRoutePostProcessingLabel => L(
        "settings.ai.cloudRoutePost",
        "Route post-processing to cloud");
    public string AiCloudProviderHint => L(
        "settings.ai.cloudHint",
        "Use an OpenAI-compatible endpoint such as OpenAI, OpenRouter, or a compatible gateway. Base URL usually ends with /v1.");

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }
    public ObservableCollection<ModelItemViewModel> Models { get; }
    public ObservableCollection<TranslationModelOption> AvailableTranslationModels { get; }
    public ObservableCollection<CloudProviderModelOption> DiscoveredCloudModels { get; }
    public bool HasDiscoveredCloudModels => DiscoveredCloudModels.Count > 0;

    public string AiSectionOllamaProvider => L("settings.ai.ollamaProvider", "Ollama (local daemon)");
    public string AiOllamaEnabledLabel => L("settings.ai.ollamaEnabled", "Enable Ollama Provider");
    public string AiOllamaBaseUrlLabel => L("settings.ai.ollamaBaseUrl", "Base URL:");
    public static string AiOllamaBaseUrlPlaceholder => "http://localhost:11434";
    public string AiOllamaTranslationModelLabel => L("settings.ai.ollamaTranslationModel", "Translation Tag:");
    public static string AiOllamaTranslationModelPlaceholder => "gemma3:4b";
    public string AiOllamaPostProcessingModelLabel => L("settings.ai.ollamaPostModel", "Post-Processing Tag:");
    public static string AiOllamaPostProcessingModelPlaceholder => "qwen3:4b";
    public string AiOllamaTestConnectionLabel => L("settings.ai.ollamaTestConnection", "Test Connection");
    public string AiOllamaDiscoveredModelsLabel => L("settings.ai.ollamaDiscoveredModels", "Pulled Models");
    public string AiOllamaUseTranslationLabel => L("settings.ai.ollamaUseTranslation", "Use for Translation");
    public string AiOllamaUsePostProcessingLabel => L("settings.ai.ollamaUsePost", "Use for Post-Processing");
    public string AiOllamaProviderHint => L(
        "settings.ai.ollamaProviderHint",
        "Ollama is a user-managed local daemon. Install Ollama, run 'ollama serve', and pre-pull models with 'ollama pull <tag>'. LiveLingo never starts the daemon or downloads models.");

    public ObservableCollection<OllamaProviderModelOption> DiscoveredOllamaModels { get; }
    public bool HasDiscoveredOllamaModels => DiscoveredOllamaModels.Count > 0;

    /// <summary>
    /// Diagnostics sub-viewmodel. Populated when telemetry is available; otherwise
    /// stays <c>null</c> and the view binds to an empty panel. Lifecycle is tied to
    /// the owning <see cref="SettingsViewModel"/> — nothing else holds a reference.
    /// </summary>
    public DiagnosticsViewModel? Diagnostics { get; }

    public SettingsViewModel(
        ISettingsService settings,
        IModelManager modelManager,
        ITranslationEngine? engine = null,
        ILogger<SettingsViewModel>? logger = null,
        IMessenger? messenger = null,
        ILocalizationService? localizationService = null,
        ILanguageCatalog? languageCatalog = null,
        CoreOptions? coreOptions = null,
        ILlmModelLoadCoordinator? llmCoordinator = null,
        IPlatformServices? platformServices = null,
        ISecretStore? secretStore = null,
        ICloudProviderRuntimeState? cloudProviderRuntimeState = null,
        ILocalModelRuntimeState? localModelRuntimeState = null,
        IOllamaProbeService? ollamaProbeService = null,
        ITranslationTelemetry? translationTelemetry = null,
        IModelDownloadCoordinator? modelDownloadCoordinator = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        var resolvedCloudRuntime = cloudProviderRuntimeState ?? new NullCloudProviderRuntimeState();
        var resolvedSecretStore = secretStore ?? new InMemorySecretStore();
        Diagnostics = translationTelemetry is null
            ? null
            : new DiagnosticsViewModel(translationTelemetry, localizationService);
        _localModelRuntimeState = localModelRuntimeState ?? new NullLocalModelRuntimeState();
        _localModelRuntimeState.StateChanged += state => LocalModelStatusMessage =
            LocalModelRuntimePresentation.BuildSettingsStatusMessage(_loc, state, _localModelRuntimeState.ActiveModelDescriptor);
        _platformServices = platformServices;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _loc = localizationService;
        _languageCatalog = languageCatalog ?? new LanguageCatalog();

        var deps = SettingsViewModelDependencies.Create(
            settings, modelManager, resolvedCloudRuntime, ollamaProbeService,
            coreOptions, llmCoordinator, resolvedSecretStore, localizationService, logger);
        _localization = deps.Localization;
        _workingCopyNormalizer = deps.Normalizer;
        _translationModelInventory = deps.ModelInventory;
        _cloudPresetCoordinator = deps.PresetCoordinator;
        _ollamaProbeOrchestrator = deps.OllamaProbe;
        _cloudProbeOrchestrator = deps.CloudProbe;
        _languagePairSyncer = deps.LanguagePairSyncer;
        _dirtyGuard = deps.DirtyGuard;
        _persistenceCoordinator = deps.Persistence;
        _cloudPresetCoordinator.PresentationChanged += RaiseCloudProviderPresentationChanged;

        InitializeLocalizedOptions();
        AvailableLanguages = _languageCatalog.All;
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
        DiscoveredCloudModels = new ObservableCollection<CloudProviderModelOption>();
        DiscoveredOllamaModels = new ObservableCollection<OllamaProviderModelOption>();
        Models = ModelItemViewModel.CreateAll(
            modelManager,
            modelDownloadCoordinator ?? NullModelDownloadCoordinator.Instance,
            localizationService,
            platformServices);
        HookWorkingCopy(WorkingCopy);
        HookModelItemChanges();
        RefreshTranslationModelsInternal();
        LoadFromSettings(_settings.Current);
        InitPermissions();
    }

    public SettingsViewModel(
        ISettingsService settings,
        ITranslationEngine? engine = null,
        IMessenger? messenger = null,
        ILocalizationService? localizationService = null,
        ILanguageCatalog? languageCatalog = null,
        CoreOptions? coreOptions = null,
        ILlmModelLoadCoordinator? llmCoordinator = null,
        ISecretStore? secretStore = null,
        ICloudProviderRuntimeState? cloudProviderRuntimeState = null,
        ILocalModelRuntimeState? localModelRuntimeState = null,
        IOllamaProbeService? ollamaProbeService = null,
        ITranslationTelemetry? translationTelemetry = null)
    {
        _settings = settings;
        _modelManager = null;
        var resolvedCloudRuntime = cloudProviderRuntimeState ?? new NullCloudProviderRuntimeState();
        var resolvedSecretStore = secretStore ?? new InMemorySecretStore();
        Diagnostics = translationTelemetry is null
            ? null
            : new DiagnosticsViewModel(translationTelemetry, localizationService);
        _localModelRuntimeState = localModelRuntimeState ?? new NullLocalModelRuntimeState();
        _localModelRuntimeState.StateChanged += state => LocalModelStatusMessage =
            LocalModelRuntimePresentation.BuildSettingsStatusMessage(_loc, state, _localModelRuntimeState.ActiveModelDescriptor);
        _platformServices = null;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _loc = localizationService;
        _languageCatalog = languageCatalog ?? new LanguageCatalog();

        var deps = SettingsViewModelDependencies.Create(
            settings, modelManager: null, resolvedCloudRuntime, ollamaProbeService,
            coreOptions, llmCoordinator, resolvedSecretStore, localizationService, logger: null);
        _localization = deps.Localization;
        _workingCopyNormalizer = deps.Normalizer;
        _translationModelInventory = deps.ModelInventory;
        _cloudPresetCoordinator = deps.PresetCoordinator;
        _ollamaProbeOrchestrator = deps.OllamaProbe;
        _cloudProbeOrchestrator = deps.CloudProbe;
        _languagePairSyncer = deps.LanguagePairSyncer;
        _dirtyGuard = deps.DirtyGuard;
        _persistenceCoordinator = deps.Persistence;
        _cloudPresetCoordinator.PresentationChanged += RaiseCloudProviderPresentationChanged;

        InitializeLocalizedOptions();
        AvailableLanguages = _languageCatalog.All;
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
        DiscoveredCloudModels = new ObservableCollection<CloudProviderModelOption>();
        DiscoveredOllamaModels = new ObservableCollection<OllamaProviderModelOption>();
        Models = [];
        HookWorkingCopy(WorkingCopy);
        RefreshTranslationModelsInternal();
        LoadFromSettings(_settings.Current);
        InitPermissions();
    }

    partial void OnWorkingCopyChanged(SettingsModel? oldValue, SettingsModel newValue)
    {
        if (oldValue is not null)
            UnhookWorkingCopy(oldValue);
        HookWorkingCopy(newValue);
    }

    private void InitPermissions()
    {
        ShowPermissionSection = OperatingSystem.IsMacOS();
    }

    private void HookWorkingCopy(SettingsModel model)
    {
        model.PropertyChanged -= OnWorkingCopyRootChanged;
        model.PropertyChanged += OnWorkingCopyRootChanged;

        HookNestedGroups(model);
    }

    private void UnhookWorkingCopy(SettingsModel model)
    {
        model.PropertyChanged -= OnWorkingCopyRootChanged;
        UnhookNestedGroups(model);
    }

    private static void HookGroup(INotifyPropertyChanged? group, PropertyChangedEventHandler handler)
    {
        if (group is null) return;
        group.PropertyChanged -= handler;
        group.PropertyChanged += handler;
    }

    private static void UnhookGroup(INotifyPropertyChanged? group, PropertyChangedEventHandler handler)
    {
        if (group is null) return;
        group.PropertyChanged -= handler;
    }

    private void HookNestedGroups(SettingsModel model)
    {
        HookGroup(model.Hotkeys, OnWorkingCopyNestedChanged);
        HookGroup(model.Translation, OnWorkingCopyNestedChanged);
        HookGroup(model.Translation.ModelPolicy, OnWorkingCopyNestedChanged);
        HookGroup(model.Translation.CloudProvider, OnWorkingCopyNestedChanged);
        HookGroup(model.Translation.OllamaProvider, OnWorkingCopyNestedChanged);
        HookGroup(model.Processing, OnWorkingCopyNestedChanged);
        HookGroup(model.Speech, OnWorkingCopyNestedChanged);
        HookGroup(model.UI, OnWorkingCopyNestedChanged);
        HookGroup(model.Update, OnWorkingCopyNestedChanged);
        HookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void UnhookNestedGroups(SettingsModel model)
    {
        UnhookGroup(model.Hotkeys, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation.ModelPolicy, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation.CloudProvider, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation.OllamaProvider, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Processing, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Speech, OnWorkingCopyNestedChanged);
        UnhookGroup(model.UI, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Update, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void OnWorkingCopyRootChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsModel.Hotkeys) or nameof(SettingsModel.Translation) or
            nameof(SettingsModel.Processing) or nameof(SettingsModel.Speech) or
            nameof(SettingsModel.UI) or nameof(SettingsModel.Update) or nameof(SettingsModel.Advanced))
        {
            UnhookNestedGroups(WorkingCopy);
            HookNestedGroups(WorkingCopy);
        }

        if (e.PropertyName == nameof(SettingsModel.Speech))
            RaiseActiveSttModelChanged();

        MarkDirtyIfNeeded();
    }

    private void OnWorkingCopyNestedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TranslationSettings translation)
        {
            if (e.PropertyName == nameof(TranslationSettings.ActiveTranslationModelId))
                _languagePairSyncer.SyncLanguagePairFromModel(
                    translation.ActiveTranslationModelId, AvailableTranslationModels, translation);
            else if (e.PropertyName is nameof(TranslationSettings.DefaultSourceLanguage) or nameof(TranslationSettings.DefaultTargetLanguage))
                _languagePairSyncer.SyncModelFromLanguagePair(
                    translation.DefaultSourceLanguage, translation.DefaultTargetLanguage,
                    AvailableTranslationModels, translation);
        }
        else if (sender is CloudProviderSettings cloudProvider)
        {
            if (e.PropertyName == nameof(CloudProviderSettings.PresetId))
                _cloudPresetCoordinator.ApplyPreset(cloudProvider);
            else if (e.PropertyName == nameof(CloudProviderSettings.BaseUrl))
                _cloudPresetCoordinator.SyncPresetFromBaseUrl(cloudProvider);

            if (e.PropertyName is nameof(CloudProviderSettings.PresetId)
                or nameof(CloudProviderSettings.BaseUrl)
                or nameof(CloudProviderSettings.ApiKey))
            {
                ClearDiscoveredCloudModels();
                CloudProviderStatusMessage = null;
            }
        }
        else if (sender is OllamaProviderSettings)
        {
            if (e.PropertyName is nameof(OllamaProviderSettings.Enabled)
                or nameof(OllamaProviderSettings.BaseUrl))
            {
                ClearDiscoveredOllamaModels();
                OllamaProviderStatusMessage = null;
            }
        }
        else if (sender is SpeechSettings)
        {
            if (e.PropertyName is nameof(SpeechSettings.RoutingMode) or nameof(SpeechSettings.ActiveModelId))
                RaiseActiveSttModelChanged();
        }

        MarkDirtyIfNeeded();
    }

    private void RaiseActiveSttModelChanged()
    {
        OnPropertyChanged(nameof(ActiveSttModelDisplayName));
        OnPropertyChanged(nameof(ActiveSttModelSizeText));
        OnPropertyChanged(nameof(IsActiveSttModelInstalled));
    }

    private ModelDescriptor ResolveActiveSttModel()
    {
        var routing = SpeechModelRouting.ParseRoutingMode(WorkingCopy.Speech.RoutingMode);
        return SpeechModelRouting.Resolve(routing, WorkingCopy.Speech.ActiveModelId);
    }

    private void MarkDirtyIfNeeded()
    {
        if (!_dirtyGuard.IsLoading && !_languagePairSyncer.IsSyncing)
            IsDirty = true;
    }

    private void LoadFromSettings(SettingsModel source)
    {
        _dirtyGuard.RunLoading(() =>
        {
            var clone = source.DeepClone();
            _workingCopyNormalizer.Normalize(clone.Translation);
            var selectedModel = _workingCopyNormalizer.ResolveInitialTranslationModel(clone.Translation, AvailableTranslationModels);
            if (selectedModel is { Type: ModelType.Translation } &&
                !string.IsNullOrWhiteSpace(selectedModel.SourceLanguage) &&
                !string.IsNullOrWhiteSpace(selectedModel.TargetLanguage))
            {
                clone.Translation.DefaultSourceLanguage = selectedModel.SourceLanguage!;
                clone.Translation.DefaultTargetLanguage = selectedModel.TargetLanguage!;
                clone.Translation.ActiveTranslationModelId = selectedModel.Id;
            }
            else if (_modelManager is not null &&
                     !string.IsNullOrWhiteSpace(clone.Translation.ActiveTranslationModelId) &&
                     !AvailableTranslationModels.Any(m =>
                         string.Equals(m.Id, clone.Translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase)))
            {
                clone.Translation.ActiveTranslationModelId = null;
            }

            clone.Translation.ModelPolicy.PreferredLocalTranslationModelId = clone.Translation.ActiveTranslationModelId;

            if (!UILanguages.Any(l => string.Equals(l.Code, clone.UI.Language, StringComparison.OrdinalIgnoreCase)))
                clone.UI.Language = UILanguages[0].Code;

            WorkingCopy = clone;
            RaiseCloudProviderPresentationChanged();
            ApplyCloudProviderProbeOutcome(_cloudProbeOrchestrator.BuildOutcomeFromCachedSnapshot(clone));
            LocalModelStatusMessage = LocalModelRuntimePresentation.BuildSettingsStatusMessage(
                _loc, _localModelRuntimeState.State, _localModelRuntimeState.ActiveModelDescriptor);
            _originalModelStoragePath = clone.Advanced.ModelStoragePath;
            IsDirty = false;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsDirty)
        {
            _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
            return;
        }

        MigrationError = null;
        var settingsBeforeSave = _settings.Current.DeepClone();

        ReconcileActiveTranslationModelForSave();
        WorkingCopy.Translation.ModelPolicy.PreferredLocalTranslationModelId =
            WorkingCopy.Translation.ActiveTranslationModelId;

        var outcome = await _persistenceCoordinator.PersistAsync(
            new SettingsPersistenceRequest(WorkingCopy, _originalModelStoragePath, settingsBeforeSave),
            CancellationToken.None);
        if (!outcome.MigrationSucceeded)
        {
            MigrationError = outcome.MigrationErrorMessage;
            return;
        }

        _originalModelStoragePath = outcome.UpdatedOriginalModelStoragePath;
        _messenger.Send(new SettingsChangedMessage());
        IsDirty = false;
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    private void ReconcileActiveTranslationModelForSave()
    {
        var activeModel = AvailableTranslationModels.FirstOrDefault(m =>
            string.Equals(m.Id, WorkingCopy.Translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
        if (activeModel is { Type: ModelType.Translation } &&
            !string.IsNullOrWhiteSpace(activeModel.SourceLanguage) &&
            !string.IsNullOrWhiteSpace(activeModel.TargetLanguage))
        {
            WorkingCopy.Translation.DefaultSourceLanguage = activeModel.SourceLanguage!;
            WorkingCopy.Translation.DefaultTargetLanguage = activeModel.TargetLanguage!;
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
            return;
        }

        if (activeModel is not null)
        {
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
            return;
        }

        var matched = AvailableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, WorkingCopy.Translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, WorkingCopy.Translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
        WorkingCopy.Translation.ActiveTranslationModelId = matched?.Id;
    }

    [RelayCommand]
    private void CheckPermissions() =>
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.ShowSettingsPermissionDialog)));

    [RelayCommand]
    private void RefreshTranslationModels() => RefreshTranslationModelsInternal();

    [RelayCommand]
    private void OpenModelsTab() => SelectedTabIndex = (int)SettingsTab.Models;

    [RelayCommand]
    private void OpenAdvancedTabForToken() => SelectedTabIndex = (int)SettingsTab.Advanced;

    [RelayCommand]
    private void OpenHuggingFaceTokenSettingsPage() =>
        _platformServices?.OpenUrl("https://huggingface.co/settings/tokens");

    [RelayCommand]
    private async Task TestCloudProviderConnectionAsync()
    {
        IsTestingCloudProvider = true;
        try
        {
            var outcome = await _cloudProbeOrchestrator.RefreshAsync(WorkingCopy, CancellationToken.None);
            ApplyCloudProviderProbeOutcome(outcome);
        }
        finally
        {
            IsTestingCloudProvider = false;
        }
    }

    [RelayCommand]
    private async Task FetchCloudProviderModelsAsync()
    {
        IsFetchingCloudModels = true;
        try
        {
            var outcome = await _cloudProbeOrchestrator.RefreshAsync(WorkingCopy, CancellationToken.None);
            ApplyCloudProviderProbeOutcome(outcome);
        }
        finally
        {
            IsFetchingCloudModels = false;
        }
    }

    [RelayCommand]
    private void UseCloudTranslationModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        WorkingCopy.Translation.CloudProvider.TranslationModelId = modelId;
    }

    [RelayCommand]
    private void UseCloudPostProcessingModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        WorkingCopy.Translation.CloudProvider.PostProcessingModelId = modelId;
    }

    [RelayCommand]
    private async Task TestOllamaProviderConnectionAsync()
    {
        IsTestingOllamaProvider = true;
        try
        {
            var outcome = await _ollamaProbeOrchestrator.ProbeAsync(
                WorkingCopy.Translation.OllamaProvider.BaseUrl,
                CancellationToken.None);
            OllamaProviderStatusMessage = outcome.Message;
            if (outcome.Kind == OllamaProbeOutcomeKind.Success)
                ApplyDiscoveredOllamaModels(outcome.Models);
            else if (outcome.Kind == OllamaProbeOutcomeKind.Failure)
                ClearDiscoveredOllamaModels();
        }
        finally
        {
            IsTestingOllamaProvider = false;
        }
    }

    [RelayCommand]
    private void UseOllamaTranslationModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        WorkingCopy.Translation.OllamaProvider.TranslationModelId = modelId;
    }

    [RelayCommand]
    private void UseOllamaPostProcessingModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        WorkingCopy.Translation.OllamaProvider.PostProcessingModelId = modelId;
    }

    [RelayCommand]
    private void OpenPrimaryTranslationModelOnHuggingFace()
    {
        if (_platformServices is null) return;
        var primaryModel = ModelRegistry.CandidateTranslationModels.Count > 0
            ? ModelRegistry.CandidateTranslationModels[0]
            : ModelRegistry.Gemma4_26B_A4B;
        if (!HuggingFaceWebUrls.TryGetModelCardUrl(primaryModel.DownloadUrl, out var url)) return;
        _platformServices.OpenUrl(url);
    }

    [RelayCommand]
    private void Reset()
    {
        var originalModelStoragePath = _originalModelStoragePath;
        LoadFromSettings(SettingsModel.CreateDefault());
        _originalModelStoragePath = originalModelStoragePath;
        IsDirty = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings(_settings.Current);
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    private CloudProviderPreset GetSelectedCloudProviderPreset() =>
        _cloudPresetCoordinator.GetSelectedPreset(WorkingCopy.Translation.CloudProvider);

    private void RaiseCloudProviderPresentationChanged()
    {
        OnPropertyChanged(nameof(AiCloudBaseUrlPlaceholder));
        OnPropertyChanged(nameof(AiCloudTranslationModelPlaceholder));
        OnPropertyChanged(nameof(AiCloudPostProcessingModelPlaceholder));
    }

    private void HookModelItemChanges()
    {
        foreach (var item in Models)
            item.PropertyChanged += OnModelItemPropertyChanged;
    }

    private void UnhookModelItemChanges()
    {
        foreach (var item in Models)
            item.PropertyChanged -= OnModelItemPropertyChanged;
    }

    private void OnModelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelItemViewModel.IsInstalled))
        {
            RefreshTranslationModelsInternal();
            RaiseActiveSttModelChanged();
        }
    }

    private void RefreshTranslationModelsInternal()
    {
        var currentSource = WorkingCopy.Translation.DefaultSourceLanguage;
        var currentTarget = WorkingCopy.Translation.DefaultTargetLanguage;
        var currentModelId = WorkingCopy.Translation.ActiveTranslationModelId;

        AvailableTranslationModels.Clear();
        foreach (var model in _translationModelInventory.Snapshot())
            AvailableTranslationModels.Add(model);
        OnPropertyChanged(nameof(ShowNoInstalledModelsHint));

        _languagePairSyncer.RestoreModelSelectionAfterRefresh(
            currentModelId, currentSource, currentTarget,
            AvailableTranslationModels, WorkingCopy.Translation);
    }

    private void InitializeLocalizedOptions()
    {
        var options = _localization.BuildSelectableOptions();
        InjectionModeOptions = options.InjectionModes;
        PostProcessModeOptions = options.PostProcessModes;
        RoutingModeOptions = options.RoutingModes;
        SttRoutingModeOptions = options.SttRoutingModes;
        CloudProviderPresetOptions = options.CloudProviderPresets;
        LogLevelOptions = options.LogLevels;
    }

    private void ApplyCloudProviderProbeOutcome(CloudProviderProbeOutcome outcome)
    {
        if (outcome.Models.Count == 0)
        {
            ClearDiscoveredCloudModels();
            CloudProviderStatusMessage = outcome.StatusMessage;
            return;
        }

        DiscoveredCloudModels.Clear();
        foreach (var model in outcome.Models)
            DiscoveredCloudModels.Add(model);
        OnPropertyChanged(nameof(HasDiscoveredCloudModels));
        CloudProviderStatusMessage = outcome.StatusMessage;
    }

    private void ClearDiscoveredCloudModels()
    {
        DiscoveredCloudModels.Clear();
        OnPropertyChanged(nameof(HasDiscoveredCloudModels));
    }

    private void ApplyDiscoveredOllamaModels(IReadOnlyList<OllamaModelInfo> models)
    {
        DiscoveredOllamaModels.Clear();
        foreach (var model in models)
            DiscoveredOllamaModels.Add(new OllamaProviderModelOption(model.Id, model.SizeBytes));
        OnPropertyChanged(nameof(HasDiscoveredOllamaModels));
    }

    private void ClearDiscoveredOllamaModels()
    {
        DiscoveredOllamaModels.Clear();
        OnPropertyChanged(nameof(HasDiscoveredOllamaModels));
    }

    private string L(string key, string fallback) => _localization.Translate(key, fallback);

    private string L(string key, string fallback, params object[] args) =>
        _localization.Translate(key, fallback, args);

    public void Dispose()
    {
        // Detach the IsInstalled listeners first so a last-moment
        // coordinator publish cannot fire a PropertyChanged into a
        // partially-disposed SettingsViewModel.
        UnhookModelItemChanges();
        foreach (var item in Models)
            item.Dispose();
        Diagnostics?.Dispose();
    }
}

public record UILanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public record TranslationModelOption(
    string Id,
    string DisplayName,
    ModelType Type,
    string? SourceLanguage,
    string? TargetLanguage,
    string PairLabel)
{
    public override string ToString() => DisplayName;
}

public record SelectableOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public record CloudProviderModelOption(string Id, string? OwnedBy)
{
    public string Caption => string.IsNullOrWhiteSpace(OwnedBy) ? Id : $"{Id} · {OwnedBy}";
    public override string ToString() => Caption;
}

public record OllamaProviderModelOption(string Id, long SizeBytes)
{
    public string Caption => SizeBytes > 0 ? $"{Id} · {FormatSize(SizeBytes)}" : Id;
    public override string ToString() => Caption;

    private static string FormatSize(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;
        return bytes >= gib
            ? $"{bytes / gib:F1} GB"
            : $"{bytes / mib:F0} MB";
    }
}
