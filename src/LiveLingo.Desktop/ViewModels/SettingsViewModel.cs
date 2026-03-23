using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly CoreOptions? _coreOptions;
    private readonly ILlmModelLoadCoordinator? _llmCoordinator;
    private readonly IPlatformServices? _platformServices;
    private readonly ILogger? _logger;
    private readonly IMessenger _messenger;
    private readonly ILocalizationService? _loc;
    private readonly ILanguageCatalog _languageCatalog;
    private string? _originalModelStoragePath;
    private bool _isLoadingWorkingCopy;
    private bool _isSyncingTranslationSelection;

    [ObservableProperty] private SettingsModel _workingCopy = SettingsModel.CreateDefault();
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _migrationError;
    [ObservableProperty] private bool _showPermissionSection;
    [ObservableProperty] private int _selectedTabIndex;

    public static IReadOnlyList<string> InjectionModes { get; } = ["PasteAndSend", "PasteOnly"];
    public static IReadOnlyList<string> PostProcessModes { get; } = ["Off", "Summarize", "Optimize", "Colloquialize"];
    public static IReadOnlyList<string> LogLevels { get; } = ["Verbose", "Debug", "Information", "Warning", "Error"];
    public IReadOnlyList<SelectableOption> InjectionModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> PostProcessModeOptions { get; private set; } = [];
    public IReadOnlyList<SelectableOption> LogLevelOptions { get; private set; } = [];
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
    public string TranslationSourceLabel => L("settings.translation.source", "Source Language:");
    public string TranslationTargetLabel => L("settings.translation.target", "Target Language:");
    public string TranslationActiveModelLabel => L("settings.translation.activeModel", "Active Model:");
    public string TranslationRefreshModelsTooltip => L("settings.translation.refreshModels", "Refresh translation models");
    public string TranslationNoInstalledModelsHint => L(
        "settings.translation.noInstalledModelsHint",
        "No downloaded model available. Go to Models tab to download.");
    public string TranslationOpenModelsTab => L("settings.translation.openModelsTab", "Go to Models");
    public bool ShowNoInstalledModelsHint => AvailableTranslationModels.Count == 0;
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

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }
    public ObservableCollection<ModelItemViewModel> Models { get; }
    public ObservableCollection<TranslationModelOption> AvailableTranslationModels { get; }

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
        IPlatformServices? platformServices = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _coreOptions = coreOptions;
        _llmCoordinator = llmCoordinator;
        _platformServices = platformServices;
        _logger = logger;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _loc = localizationService;
        _languageCatalog = languageCatalog ?? new LanguageCatalog();
        InitializeLocalizedOptions();
        AvailableLanguages = _languageCatalog.All;
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
        Models = ModelItemViewModel.CreateAll(modelManager, localizationService, platformServices);
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
        ILlmModelLoadCoordinator? llmCoordinator = null)
    {
        _settings = settings;
        _modelManager = null;
        _logger = null;
        _coreOptions = coreOptions;
        _llmCoordinator = llmCoordinator;
        _platformServices = null;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _loc = localizationService;
        _languageCatalog = languageCatalog ?? new LanguageCatalog();
        InitializeLocalizedOptions();
        AvailableLanguages = _languageCatalog.All;
        AvailableTranslationModels = new ObservableCollection<TranslationModelOption>();
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
        HookGroup(model.Processing, OnWorkingCopyNestedChanged);
        HookGroup(model.UI, OnWorkingCopyNestedChanged);
        HookGroup(model.Update, OnWorkingCopyNestedChanged);
        HookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void UnhookNestedGroups(SettingsModel model)
    {
        UnhookGroup(model.Hotkeys, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Translation, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Processing, OnWorkingCopyNestedChanged);
        UnhookGroup(model.UI, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Update, OnWorkingCopyNestedChanged);
        UnhookGroup(model.Advanced, OnWorkingCopyNestedChanged);
    }

    private void OnWorkingCopyRootChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsModel.Hotkeys) or nameof(SettingsModel.Translation) or
            nameof(SettingsModel.Processing) or nameof(SettingsModel.UI) or
            nameof(SettingsModel.Update) or nameof(SettingsModel.Advanced))
        {
            UnhookNestedGroups(WorkingCopy);
            HookNestedGroups(WorkingCopy);
        }

        MarkDirtyIfNeeded();
    }

    private void OnWorkingCopyNestedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TranslationSettings translation)
        {
            if (e.PropertyName == nameof(TranslationSettings.ActiveTranslationModelId))
                OnActiveTranslationModelIdChanged(translation.ActiveTranslationModelId);
            else if (e.PropertyName is nameof(TranslationSettings.DefaultSourceLanguage) or nameof(TranslationSettings.DefaultTargetLanguage))
                SyncModelSelectionFromLanguagePair(translation.DefaultSourceLanguage, translation.DefaultTargetLanguage);
        }

        MarkDirtyIfNeeded();
    }

    private void MarkDirtyIfNeeded()
    {
        if (!_isLoadingWorkingCopy && !_isSyncingTranslationSelection)
            IsDirty = true;
    }

    private void LoadFromSettings(SettingsModel source)
    {
        _isLoadingWorkingCopy = true;
        try
        {
            var clone = source.DeepClone();
            var selectedModel = ResolveInitialTranslationModel(clone.Translation);
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

            if (!UILanguages.Any(l => string.Equals(l.Code, clone.UI.Language, StringComparison.OrdinalIgnoreCase)))
                clone.UI.Language = UILanguages[0].Code;

            WorkingCopy = clone;
            _originalModelStoragePath = clone.Advanced.ModelStoragePath;
            IsDirty = false;
        }
        finally
        {
            _isLoadingWorkingCopy = false;
        }
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
        var advancedBefore = _settings.Current.Advanced.DeepClone();
        var translationBefore = _settings.Current.Translation.DeepClone();
        var oldPath = CoreOptionsSync.NormalizePathForCompare(_originalModelStoragePath);
        var newPath = CoreOptionsSync.NormalizePathForCompare(WorkingCopy.Advanced.ModelStoragePath);
        if (_modelManager is not null && !string.IsNullOrEmpty(newPath) &&
            !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _modelManager.MigrateStoragePathAsync(newPath);
                _originalModelStoragePath = WorkingCopy.Advanced.ModelStoragePath;
            }
            catch (Exception ex)
            {
                MigrationError = L("settings.advanced.migrationFailed", "Migration failed: {0}", ex.Message);
                _logger?.LogError(ex, "Failed to migrate model storage path");
                return;
            }
        }

        var activeModel = AvailableTranslationModels.FirstOrDefault(m =>
            string.Equals(m.Id, WorkingCopy.Translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
        if (activeModel is { Type: ModelType.Translation } &&
            !string.IsNullOrWhiteSpace(activeModel.SourceLanguage) &&
            !string.IsNullOrWhiteSpace(activeModel.TargetLanguage))
        {
            WorkingCopy.Translation.DefaultSourceLanguage = activeModel.SourceLanguage!;
            WorkingCopy.Translation.DefaultTargetLanguage = activeModel.TargetLanguage!;
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
        }
        else if (activeModel is not null)
        {
            WorkingCopy.Translation.ActiveTranslationModelId = activeModel.Id;
        }
        else
        {
            var matched = AvailableTranslationModels.FirstOrDefault(m =>
                m.Type == ModelType.Translation &&
                string.Equals(m.SourceLanguage, WorkingCopy.Translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.TargetLanguage, WorkingCopy.Translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
            WorkingCopy.Translation.ActiveTranslationModelId = matched?.Id;
        }

        _settings.Replace(WorkingCopy);
        if (_coreOptions is not null)
            CoreOptionsSync.ApplyFromSettings(WorkingCopy, _coreOptions, _modelManager);

        var translationModelChanged = !string.Equals(translationBefore.ActiveTranslationModelId, WorkingCopy.Translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase);

        if (_llmCoordinator is not null &&
            (CoreOptionsSync.AdvancedSettingsAffectLlmLoad(advancedBefore, WorkingCopy.Advanced) || translationModelChanged))
            await _llmCoordinator.RequestRetryPrimaryTranslationModelAsync(CancellationToken.None).ConfigureAwait(false);
        _messenger.Send(new SettingsChangedMessage());
        IsDirty = false;
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    [RelayCommand]
    private void CheckPermissions() =>
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.ShowSettingsPermissionDialog)));

    [RelayCommand]
    private void RefreshTranslationModels() => RefreshTranslationModelsInternal();

    [RelayCommand]
    private void OpenModelsTab() => SelectedTabIndex = 2;

    [RelayCommand]
    private void OpenAdvancedTabForToken() => SelectedTabIndex = 3;

    [RelayCommand]
    private void OpenHuggingFaceTokenSettingsPage() =>
        _platformServices?.OpenUrl("https://huggingface.co/settings/tokens");

    [RelayCommand]
    private void OpenPrimaryTranslationModelOnHuggingFace()
    {
        if (_platformServices is null) return;
        if (!HuggingFaceWebUrls.TryGetModelCardUrl(ModelRegistry.Qwen35_9B.DownloadUrl, out var url)) return;
        _platformServices.OpenUrl(url);
    }

    [RelayCommand]
    private void Reset() => LoadFromSettings(SettingsModel.CreateDefault());

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings(_settings.Current);
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSettings)));
    }

    private void OnActiveTranslationModelIdChanged(string? modelId)
    {
        if (_isSyncingTranslationSelection || string.IsNullOrWhiteSpace(modelId))
            return;

        var selected = AvailableTranslationModels.FirstOrDefault(m =>
            string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (selected is null ||
            selected.Type != ModelType.Translation ||
            string.IsNullOrWhiteSpace(selected.SourceLanguage) ||
            string.IsNullOrWhiteSpace(selected.TargetLanguage))
        {
            return;
        }

        _isSyncingTranslationSelection = true;
        try
        {
            WorkingCopy.Translation.DefaultSourceLanguage = selected.SourceLanguage!;
            WorkingCopy.Translation.DefaultTargetLanguage = selected.TargetLanguage!;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private void SyncModelSelectionFromLanguagePair(string sourceLanguage, string targetLanguage)
    {
        if (_isSyncingTranslationSelection)
            return;

        var matched = AvailableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase));

        _isSyncingTranslationSelection = true;
        try
        {
            WorkingCopy.Translation.ActiveTranslationModelId = matched?.Id;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private TranslationModelOption? ResolveInitialTranslationModel(TranslationSettings translation)
    {
        if (!string.IsNullOrWhiteSpace(translation.ActiveTranslationModelId))
        {
            var byId = AvailableTranslationModels.FirstOrDefault(m =>
                string.Equals(m.Id, translation.ActiveTranslationModelId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        return AvailableTranslationModels.FirstOrDefault(m =>
            m.Type == ModelType.Translation &&
            string.Equals(m.SourceLanguage, translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.TargetLanguage, translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
    }

    private void HookModelItemChanges()
    {
        foreach (var item in Models)
            item.PropertyChanged += OnModelItemPropertyChanged;
    }

    private void OnModelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelItemViewModel.IsInstalled))
            RefreshTranslationModelsInternal();
    }

    private void RefreshTranslationModelsInternal()
    {
        var currentSource = WorkingCopy.Translation.DefaultSourceLanguage;
        var currentTarget = WorkingCopy.Translation.DefaultTargetLanguage;
        var currentModelId = WorkingCopy.Translation.ActiveTranslationModelId;

        var installedModels = (_modelManager?.ListInstalled() ?? [])
            .Where(m => m.Type == ModelType.Translation)
            .Select(CreateInstalledTranslationModelOption)
            .Where(m => m is not null)
            .Select(m => m!)
            .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase);

        var ordered = installedModels
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailableTranslationModels.Clear();
        foreach (var model in ordered)
            AvailableTranslationModels.Add(model);
        OnPropertyChanged(nameof(ShowNoInstalledModelsHint));

        _isSyncingTranslationSelection = true;
        try
        {
            var restored = !string.IsNullOrWhiteSpace(currentModelId)
                ? AvailableTranslationModels.FirstOrDefault(m =>
                    string.Equals(m.Id, currentModelId, StringComparison.OrdinalIgnoreCase))
                : null;
            restored ??= AvailableTranslationModels.FirstOrDefault(m =>
                m.Type == ModelType.Translation &&
                string.Equals(m.SourceLanguage, currentSource, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.TargetLanguage, currentTarget, StringComparison.OrdinalIgnoreCase));
            WorkingCopy.Translation.ActiveTranslationModelId = restored?.Id;
        }
        finally
        {
            _isSyncingTranslationSelection = false;
        }
    }

    private TranslationModelOption? CreateInstalledTranslationModelOption(InstalledModel installed)
    {
        if (installed.Type == ModelType.Translation)
        {
            string? source = null;
            string? target = null;
            if (TryParseLanguagePairFromModelId(installed.Id, out var parsedSource, out var parsedTarget))
            {
                source = parsedSource;
                target = parsedTarget;
            }

            return new TranslationModelOption(
                installed.Id,
                installed.DisplayName,
                ModelType.Translation,
                source,
                target,
                BuildPairLabel(ModelType.Translation, source, target));
        }

        return null;
    }

    private static bool TryParseLanguagePairFromModelId(string modelId, out string sourceLanguage, out string targetLanguage)
    {
        sourceLanguage = string.Empty;
        targetLanguage = string.Empty;

        const string prefix = "opus-mt-";
        if (!modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var pairPart = modelId[prefix.Length..];
        var parts = pairPart.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        sourceLanguage = parts[0];
        targetLanguage = parts[1];
        return true;
    }

    private void InitializeLocalizedOptions()
    {
        InjectionModeOptions =
        [
            new SelectableOption("PasteAndSend", L("settings.injectMode.pasteAndSend", "Paste & Send")),
            new SelectableOption("PasteOnly", L("settings.injectMode.pasteOnly", "Paste Only"))
        ];

        PostProcessModeOptions =
        [
            new SelectableOption("Off", L("settings.postMode.off", "Off")),
            new SelectableOption("Summarize", L("settings.postMode.summarize", "Summarize")),
            new SelectableOption("Optimize", L("settings.postMode.optimize", "Optimize")),
            new SelectableOption("Colloquialize", L("settings.postMode.colloquialize", "Colloquialize"))
        ];

        LogLevelOptions =
        [
            new SelectableOption("Verbose", L("settings.logLevel.verbose", "Verbose")),
            new SelectableOption("Debug", L("settings.logLevel.debug", "Debug")),
            new SelectableOption("Information", L("settings.logLevel.information", "Information")),
            new SelectableOption("Warning", L("settings.logLevel.warning", "Warning")),
            new SelectableOption("Error", L("settings.logLevel.error", "Error"))
        ];
    }

    private string BuildPairLabel(ModelType type, string? source, string? target) =>
        type == ModelType.Translation && !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target)
            ? $"{source}→{target}"
            : type == ModelType.PostProcessing
                ? L("settings.translation.pair.postProcessing", "Post-processing")
                : type.ToString();

    private string L(string key, string fallback) => _loc?.T(key) ?? fallback;
    private string L(string key, string fallback, params object[] args) => _loc?.T(key, args) ?? string.Format(fallback, args);
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
