using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly IMessenger _messenger;
    private readonly ILogger<SetupWizardViewModel>? _logger;
    private readonly ILocalizationService? _localization;
    private readonly IClipboardService? _clipboard;
    private CancellationTokenSource? _downloadCts;

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
    public string Step2Title => T("wizard.step2.title", "Download Required Models");
    public string Step2Description => T(
        "wizard.step2.description",
        "LiveLingo requires the baseline translation model for your selected language pair. This is a one-time download.");
    public string Step2CardTitle => T("wizard.step2.card.title", "Qwen3.5 9B");
    public string Step2CardSubtitle => T("wizard.step2.card.subtitle", "Baseline translation model (required)");
    public string Step2DownloadButton => T("wizard.step2.downloadButton", "Download");
    public string Step2ReadyLabel => T("wizard.step2.ready", "✓ Ready");
    public string Step2CancelButton => T("wizard.step2.cancelButton", "Cancel");
    public string CopyUrlButtonLabel => T("wizard.download.copyUrl", "Copy URL");

    public SetupWizardViewModel(
        ISettingsService settings,
        IModelManager? modelManager = null,
        int startStep = 0,
        IMessenger? messenger = null,
        ILogger<SetupWizardViewModel>? logger = null,
        ILocalizationService? localization = null,
        ILanguageCatalog? languageCatalog = null,
        IClipboardService? clipboard = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _logger = logger;
        _localization = localization;
        _clipboard = clipboard;
        TotalSteps = 3;
        StartStep = startStep;
        _currentStep = startStep;
        AvailableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
        SelectedSourceLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, SourceLanguage, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];
        SelectedTargetLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, TargetLanguage, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[1];

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

        IsDownloading = true;
        HasError = false;
        DownloadProgress = 0;
        DownloadStatus = T("wizard.download.preparing", "Preparing downloads...");
        _downloadCts = new CancellationTokenSource();

        try
        {
            var requiredModels = GetRequiredModelsForCurrentPair();
            if (requiredModels.Count == 0)
            {
                IsModelInstalled = true;
                DownloadProgress = 100;
                DownloadStatus = T("wizard.download.noneRequired", "No required models.");
                return;
            }

            for (var index = 0; index < requiredModels.Count; index++)
            {
                var descriptor = requiredModels[index];
                var modelIndex = index;

                DownloadProgress = 0;
                DownloadStatus = T(
                    "wizard.download.modelProgress",
                    "{0} ({1}/{2}) {3:F0}%",
                    descriptor.DisplayName,
                    modelIndex + 1,
                    requiredModels.Count,
                    0d);
                _logger?.LogInformation(
                    "Setup wizard model download started: {ModelId} ({Current}/{Total})",
                    descriptor.Id,
                    modelIndex + 1,
                    requiredModels.Count);

                var progress = new Progress<ModelDownloadProgress>(p =>
                {
                    var modelProgress = Math.Clamp(p.Percentage, 0, 100);
                    DownloadProgress = modelProgress;
                    DownloadStatus = T(
                        "wizard.download.modelProgress",
                        "{0} ({1}/{2}) {3:F0}%",
                        descriptor.DisplayName,
                        modelIndex + 1,
                        requiredModels.Count,
                        modelProgress);
                });

                await _modelManager.EnsureModelAsync(descriptor, progress, _downloadCts.Token);
                DownloadProgress = 100;
                DownloadStatus = T(
                    "wizard.download.modelDone",
                    "{0} ({1}/{2}) done",
                    descriptor.DisplayName,
                    modelIndex + 1,
                    requiredModels.Count);
                _logger?.LogInformation(
                    "Setup wizard model download completed: {ModelId} ({Current}/{Total})",
                    descriptor.Id,
                    modelIndex + 1,
                    requiredModels.Count);
            }

            IsModelInstalled = true;
            HasError = false;
            DownloadStatus = T("wizard.download.complete", "Download complete ✓");
        }
        catch (OperationCanceledException)
        {
            HasError = false;
            DownloadStatus = T("wizard.download.cancelled", "Cancelled");
            _logger?.LogWarning("Setup wizard model download cancelled by user.");
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("401"))
        {
            HasError = true;
            DownloadStatus = T("wizard.download.errorAuth", "Download failed. This model may require authorization on HuggingFace. You can download it manually from https://huggingface.co/Qwen and place the .gguf file in your models directory, or configure a HuggingFace token/mirror in the settings.");
            _logger?.LogError(ex, "Setup wizard model download failed with HTTP error.");
        }
        catch (Exception ex)
        {
            HasError = true;
            DownloadStatus = T("wizard.download.error", "Download failed. You can download it manually from https://huggingface.co/Qwen and place it in the models directory.", ex.Message);
            _logger?.LogError(ex, "Setup wizard model download failed.");
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        if (_clipboard is null) return;
        var requiredModels = GetRequiredModelsForCurrentPair();
        if (requiredModels.Count > 0)
        {
            await _clipboard.SetTextAsync(requiredModels[0].DownloadUrl);
        }
    }

    [RelayCommand]
    private void Finish()
    {
        var workingCopy = _settings.CloneCurrent();
        workingCopy.Hotkeys.OverlayToggle = OverlayHotkey;
        workingCopy.Translation.DefaultSourceLanguage = SourceLanguage;
        workingCopy.Translation.DefaultTargetLanguage = TargetLanguage;
        workingCopy.Translation.ActiveTranslationModelId =
            ModelRegistry.FindTranslationModel(SourceLanguage, TargetLanguage)?.Id;
        workingCopy.Translation.LanguagePairs = [new LanguagePair(SourceLanguage, TargetLanguage)];

        _settings.Replace(workingCopy);
        _messenger.Send(new SettingsChangedMessage());
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseSetupWizard)));
    }

    private IReadOnlyList<ModelDescriptor> GetRequiredModelsForCurrentPair() =>
        ModelRegistry.GetRequiredModelsForLanguagePair(SourceLanguage, TargetLanguage);

    private void RefreshModelInstalledState()
    {
        if (_modelManager is null)
        {
            IsModelInstalled = false;
            return;
        }

        var installedIds = _modelManager.ListInstalled()
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IsModelInstalled = GetRequiredModelsForCurrentPair()
            .All(model => installedIds.Contains(model.Id));
    }

    private string T(string key, string fallback, params object[] args)
    {
        if (_localization is not null)
            return _localization.T(key, args);
        return args.Length == 0 ? fallback : string.Format(fallback, args);
    }
}
