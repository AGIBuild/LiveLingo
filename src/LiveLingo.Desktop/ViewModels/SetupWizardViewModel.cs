using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.ViewModels.Settings;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

public partial class SetupWizardViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly IModelDownloadCoordinator _downloadCoordinator;
    private readonly IMessenger _messenger;
    private readonly ILogger<SetupWizardViewModel>? _logger;
    private readonly ILocalizationService? _localization;
    private readonly IClipboardService? _clipboard;
    private readonly CoreOptions? _coreOptions;
    private readonly ILlmModelLoadCoordinator? _llmCoordinator;
    private readonly IPlatformServices? _platform;
    private readonly IModelCatalog _modelCatalog;
    private readonly Action<ModelDownloadState> _onCoordinatorStateChanged;
    private readonly SynchronizationContext? _uiContext;
    // The wizard downloads required models sequentially; the active descriptor /
    // index pair lets coordinator state events update the right "(N/M) percent"
    // status text without a separate per-model subscription.
    private ModelDescriptor? _activeDescriptor;
    private int _activeIndex;
    private int _activeTotal;
    private bool _disposed;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _sourceLanguage = "zh";
    [ObservableProperty] private string _targetLanguage = "en";
    [ObservableProperty] private string _overlayHotkey = "Ctrl+Alt+T";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _downloadStatus;
    [ObservableProperty] private bool _isModelInstalled;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private LanguageInfo? _selectedSourceLanguage;
    [ObservableProperty] private LanguageInfo? _selectedTargetLanguage;
    [ObservableProperty] private ModelDescriptor? _selectedCandidateModel;

    public int TotalSteps { get; }
    public int StartStep { get; }
    public int DisplayStep => CurrentStep + 1;
    public string WindowTitle => T("wizard.window.title", "LiveLingo Setup");
    public string BackButtonLabel => T("wizard.nav.back", "Back");
    public string NextButtonLabel => T("wizard.nav.next", "Next");
    public string FinishButtonLabel => T("wizard.nav.finish", "Finish");
    public string StepIndicator => T("wizard.stepIndicator", "Step {0} of {1}", DisplayStep, TotalSteps);
    public string Step0Title => T("wizard.step0.title", "Choose Languages");
    public string Step0Description => T(
        "wizard.step0.description",
        "Select the source language you type in and the target language for translation.");
    public string Step0SourceLabel => T("wizard.step0.source", "Source:");
    public string Step0TargetLabel => T("wizard.step0.target", "Target:");
    public string Step1Title => T("wizard.step1.title", "Set Hotkey");
    public string Step1Description => T(
        "wizard.step1.description",
        "This keyboard shortcut opens the translation overlay. You can change it later in settings.");
    public string Step1HotkeyLabel => T("wizard.step1.hotkey", "Hotkey:");
    public bool CanGoBack => CurrentStep > StartStep;
    public bool CanGoNext => CurrentStep < TotalSteps - 1;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;
    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }
    public string Step2Title => T("wizard.step2.title", "Download Translation Model");
    public string Step2Description => T(
        "wizard.step2.description",
        "Choose and download a translation model. Any model from the list below is sufficient for translation to work.");
    public string Step2CardTitle => SelectedCandidateModel?.DisplayName ?? T("wizard.step2.card.title", "Translation Model");
    public string Step2CardSubtitle => T("wizard.step2.card.subtitle", "Translation model");
    public string Step2DownloadButton => T("wizard.step2.downloadButton", "Download");
    public string Step2ReadyLabel => T("wizard.step2.ready", "✓ Ready");
    public string Step2CancelButton => T("wizard.step2.cancelButton", "Cancel");
    public string CopyUrlButtonLabel => T("wizard.download.copyUrl", "Copy URL");
    public IReadOnlyList<ModelDescriptor> CandidateModels { get; } = ModelRegistry.CandidateTranslationModels;
    public string Step2HuggingFaceIntroHint => T(
        "wizard.step2.huggingFace.intro",
        "This download uses Hugging Face. If the model is gated or the download fails with access denied, add a read access token under Settings → Advanced (create one at huggingface.co/settings/tokens), click Save, then retry.");
    public string Step2HuggingFaceTokenMissingHint => T(
        "wizard.step2.huggingFace.missingToken",
        "No access token is configured yet. Open Advanced settings below, paste your token, save, then return here and tap Download again.");
    public string Step2HuggingFaceTokenOkHint => T(
        "wizard.step2.huggingFace.tokenOk",
        "An access token is present in your saved settings; it will be sent with this download.");
    public string Step2OpenAdvancedForTokenLabel => T("wizard.step2.huggingFace.openAdvanced", "Open Settings → Advanced…");
    public string Step2OpenModelOnHuggingFaceLabel => T(
        "wizard.step2.huggingFace.openModelPage",
        "Open model page (accept access if required)…");
    public bool ShowOpenRequiredModelOnHuggingFace =>
        _platform is not null
        && SelectedCandidateModel is not null
        && HuggingFaceWebUrls.TryGetModelCardUrl(SelectedCandidateModel.DownloadUrl, out _);
    public bool ShowOpenModelPageOnDownloadFailure => HasError && ShowOpenRequiredModelOnHuggingFace;
    public bool HasHuggingFaceTokenConfigured => !string.IsNullOrWhiteSpace(_coreOptions?.HuggingFaceToken);
    public bool ShowHuggingFaceTokenMissingCallout => !HasHuggingFaceTokenConfigured;

    public SetupWizardViewModel(
        ISettingsService settings,
        IModelManager? modelManager = null,
        int startStep = 0,
        IMessenger? messenger = null,
        ILogger<SetupWizardViewModel>? logger = null,
        ILocalizationService? localization = null,
        ILanguageCatalog? languageCatalog = null,
        IClipboardService? clipboard = null,
        CoreOptions? coreOptions = null,
        ILlmModelLoadCoordinator? llmCoordinator = null,
        IPlatformServices? platformServices = null,
        IModelCatalog? modelCatalog = null,
        IModelDownloadCoordinator? downloadCoordinator = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _coreOptions = coreOptions;
        _llmCoordinator = llmCoordinator;
        _platform = platformServices;
        _modelCatalog = modelCatalog ?? new StaticModelCatalog();
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _logger = logger;
        _localization = localization;
        _clipboard = clipboard;
        // The wizard always observes the global coordinator so a download
        // started here remains visible to Settings → Models, the overlay STT
        // download link, and any future surface that subscribes. Tests that
        // omit a coordinator get the no-op singleton.
        _downloadCoordinator = downloadCoordinator ?? NullModelDownloadCoordinator.Instance;
        _uiContext = SynchronizationContext.Current;
        TotalSteps = 3;
        StartStep = startStep;
        _currentStep = startStep;
        AvailableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
        SelectedSourceLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, SourceLanguage, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];
        SelectedTargetLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, TargetLanguage, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[1];
        SelectedCandidateModel = CandidateModels.FirstOrDefault();

        _messenger.Register<SetupWizardViewModel, SettingsChangedMessage>(
            this,
            static (r, _) => r.RefreshHuggingFaceTokenUiState());

        _onCoordinatorStateChanged = OnCoordinatorStateChanged;
        _downloadCoordinator.StateChanged += _onCoordinatorStateChanged;

        RefreshModelInstalledState();
    }

    private void RefreshHuggingFaceTokenUiState()
    {
        OnPropertyChanged(nameof(HasHuggingFaceTokenConfigured));
        OnPropertyChanged(nameof(ShowHuggingFaceTokenMissingCallout));
    }

    [RelayCommand]
    private void OpenAdvancedForHuggingFace() =>
        _messenger.Send(new AppUiRequestMessage(
            new AppUiRequest(this, AppUiRequestKind.OpenSettings, (int)SettingsTab.Advanced)));

    [RelayCommand]
    private void OpenRequiredModelOnHuggingFace()
    {
        if (_platform is null || SelectedCandidateModel is null) return;
        if (HuggingFaceWebUrls.TryGetModelCardUrl(SelectedCandidateModel.DownloadUrl, out var url))
            _platform.OpenUrl(url);
    }

    partial void OnHasErrorChanged(bool value) =>
        OnPropertyChanged(nameof(ShowOpenModelPageOnDownloadFailure));

    partial void OnSelectedCandidateModelChanged(ModelDescriptor? value)
    {
        OnPropertyChanged(nameof(Step2CardTitle));
        OnPropertyChanged(nameof(ShowOpenRequiredModelOnHuggingFace));
        RefreshModelInstalledState();
    }

    partial void OnSourceLanguageChanged(string value) => RefreshModelInstalledState();
    partial void OnTargetLanguageChanged(string value) => RefreshModelInstalledState();
    partial void OnSelectedSourceLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;
        if (!string.Equals(SourceLanguage, value.Code, StringComparison.OrdinalIgnoreCase))
            SourceLanguage = value.Code;
    }

    partial void OnSelectedTargetLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;
        if (!string.Equals(TargetLanguage, value.Code, StringComparison.OrdinalIgnoreCase))
            TargetLanguage = value.Code;
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayStep));
        OnPropertyChanged(nameof(StepIndicator));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack) CurrentStep--;
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CanGoNext) CurrentStep++;
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (_modelManager is null || IsDownloading || IsModelInstalled) return;
        if (SelectedCandidateModel is null) return;

        IsDownloading = true;
        HasError = false;
        DownloadProgress = 0;
        RefreshHuggingFaceTokenUiState();
        DownloadStatus = T("wizard.download.preparing", "Preparing downloads...");

        try
        {
            var descriptor = SelectedCandidateModel;
            _activeDescriptor = descriptor;
            _activeIndex = 0;
            _activeTotal = 1;

            DownloadProgress = 0;
            DownloadStatus = FormatProgressStatus(descriptor, 0, 1, 0d);
            _logger?.LogInformation(
                "Setup wizard model download started: {ModelId}",
                descriptor.Id);

            await _downloadCoordinator.StartAsync(descriptor);

            var finalState = _downloadCoordinator.GetState(descriptor.Id);
            if (finalState.Status == ModelDownloadStatus.Cancelled)
            {
                HasError = false;
                DownloadStatus = T("wizard.download.cancelled", "Cancelled");
                _logger?.LogWarning("Setup wizard model download cancelled by user.");
                return;
            }

            if (finalState.Status == ModelDownloadStatus.Failed)
            {
                HasError = true;
                if (finalState.ErrorMessage == ModelDownloadErrorCodes.HuggingFaceAuthorization)
                {
                    DownloadStatus = T(
                        "wizard.download.errorAuth",
                        "Download failed: Hugging Face access denied. Add a read token under Settings → Advanced (huggingface.co/settings/tokens), click Save, then retry.");
                    _logger?.LogError(
                        "Setup wizard model download failed: Hugging Face authorization for {ModelId}.",
                        descriptor.Id);
                }
                else
                {
                    DownloadStatus = T(
                        "wizard.download.error",
                        "Download failed. You can download it manually from Hugging Face and place it in the models directory.",
                        finalState.ErrorMessage ?? string.Empty);
                    _logger?.LogError(
                        "Setup wizard model download failed: {ModelId} reason={Reason}",
                        descriptor.Id,
                        finalState.ErrorMessage);
                }
                return;
            }

            DownloadProgress = 100;
            DownloadStatus = T(
                "wizard.download.modelDone",
                "{0} done",
                descriptor.DisplayName);
            _logger?.LogInformation(
                "Setup wizard model download completed: {ModelId}",
                descriptor.Id);

            IsModelInstalled = true;
            HasError = false;
            DownloadStatus = T("wizard.download.complete", "Download complete ✓");
        }
        finally
        {
            _activeDescriptor = null;
            IsDownloading = false;
        }
    }
    [RelayCommand]
    private void CancelDownload()
    {
        var current = _activeDescriptor;
        if (current is not null)
            _downloadCoordinator.Cancel(current.Id);
    }

    private void OnCoordinatorStateChanged(ModelDownloadState state)
    {
        var current = _activeDescriptor;
        if (current is null || !string.Equals(state.ModelId, current.Id, StringComparison.Ordinal))
            return;

        if (_uiContext is null)
        {
            ApplyCoordinatorState(state, current);
            return;
        }

        _uiContext.Post(_ => ApplyCoordinatorState(state, current), null);
    }

    private void ApplyCoordinatorState(ModelDownloadState state, ModelDescriptor current)
    {
        if (_disposed) return;
        // Same model that the active loop iteration is awaiting — only progress
        // updates need wiring; terminal states are handled by the awaiting task.
        if (state.Status != ModelDownloadStatus.Downloading) return;

        var pct = Math.Clamp(state.Percentage, 0, 100);
        DownloadProgress = pct;
        DownloadStatus = FormatProgressStatus(current, _activeIndex, _activeTotal, pct);
    }

    private string FormatProgressStatus(ModelDescriptor descriptor, int index, int total, double percentage) =>
        T(
            "wizard.download.modelProgress",
            "{0} ({1}/{2}) {3:F0}%",
            descriptor.DisplayName,
            index + 1,
            total,
            percentage);

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        if (_clipboard is null || SelectedCandidateModel is null) return;
        await _clipboard.SetTextAsync(SelectedCandidateModel.DownloadUrl);
    }

    [RelayCommand]
    private async Task Finish()
    {
        var advancedBefore = _settings.Current.Advanced.DeepClone();
        var workingCopy = _settings.CloneCurrent();
        workingCopy.Hotkeys.OverlayToggle = OverlayHotkey;
        workingCopy.Translation.DefaultSourceLanguage = SourceLanguage;
        workingCopy.Translation.DefaultTargetLanguage = TargetLanguage;
        workingCopy.Translation.ActiveTranslationModelId =
            ModelSelectionPolicy.SelectTranslationProfile(_modelCatalog, null, SourceLanguage, TargetLanguage).Id;
        workingCopy.Translation.ModelPolicy.PreferredLocalTranslationModelId =
            workingCopy.Translation.ActiveTranslationModelId;
        workingCopy.Translation.LanguagePairs = [new LanguagePair(SourceLanguage, TargetLanguage)];

        _settings.Replace(workingCopy);
        if (_coreOptions is not null)
            CoreOptionsSync.ApplyFromSettings(workingCopy, _coreOptions, _modelManager);
        if (_llmCoordinator is not null &&
            (CoreOptionsSync.AdvancedSettingsAffectLlmLoad(advancedBefore, workingCopy.Advanced) || IsModelInstalled))
            await _llmCoordinator.RequestRetryPrimaryTranslationModelAsync(CancellationToken.None);
        _messenger.Send(new SettingsChangedMessage());
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSetupWizard)));
    }

    private IReadOnlyList<ModelDescriptor> GetSelectedModelsForDownload() =>
        SelectedCandidateModel is not null ? [SelectedCandidateModel] : [];

    private void RefreshModelInstalledState()
    {
        if (_modelManager is null)
        {
            IsModelInstalled = false;
            return;
        }

        var installed = _modelManager.ListInstalled();
        IsModelInstalled = ModelRegistry.HasAnyTranslationModelInstalled(
            installed,
            descriptor => _modelManager.HasAllExpectedLocalAssets(descriptor));
    }

    private string T(string key, string fallback, params object[] args)
    {
        if (_localization is not null)
            return _localization.T(key, args);
        return args.Length == 0 ? fallback : string.Format(fallback, args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _downloadCoordinator.StateChanged -= _onCoordinatorStateChanged;
        _messenger.Unregister<SettingsChangedMessage>(this);
        GC.SuppressFinalize(this);
    }
}
