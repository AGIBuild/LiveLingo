using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _sourceLanguage = "zh";
    [ObservableProperty] private string _targetLanguage = "en";
    [ObservableProperty] private string _overlayHotkey = "Ctrl+Alt+T";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _downloadStatus;
    [ObservableProperty] private bool _isModelInstalled;
    [ObservableProperty] private LanguageInfo? _selectedSourceLanguage;
    [ObservableProperty] private LanguageInfo? _selectedTargetLanguage;

    public int TotalSteps { get; }
    public int StartStep { get; }
    public int DisplayStep => CurrentStep + 1;
    public bool CanGoBack => CurrentStep > StartStep;
    public bool CanGoNext => CurrentStep < TotalSteps - 1;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;
    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    public SetupWizardViewModel(
        ISettingsService settings,
        IModelManager? modelManager = null,
        int startStep = 0,
        IMessenger? messenger = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        TotalSteps = 3;
        StartStep = startStep;
        _currentStep = startStep;
        AvailableLanguages =
        [
            new("zh", "Chinese (中文)"),
            new("en", "English"),
            new("ja", "Japanese (日本語)"),
            new("ko", "Korean (한국어)"),
            new("fr", "French"),
            new("de", "German"),
            new("es", "Spanish"),
            new("ru", "Russian"),
            new("ar", "Arabic"),
            new("pt", "Portuguese"),
        ];
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
        DownloadProgress = 0;
        DownloadStatus = "Downloading…";
        _downloadCts = new CancellationTokenSource();

        try
        {
            var requiredModels = GetRequiredModelsForCurrentPair();
            for (var index = 0; index < requiredModels.Count; index++)
            {
                var descriptor = requiredModels[index];
                var modelIndex = index;
                var progress = new Progress<ModelDownloadProgress>(p =>
                {
                    var overall = ((modelIndex + (p.Percentage / 100.0)) / requiredModels.Count) * 100.0;
                    DownloadProgress = overall;
                    DownloadStatus = $"Downloading {descriptor.DisplayName}… {overall:F0}%";
                });
                await _modelManager.EnsureModelAsync(descriptor, progress, _downloadCts.Token);
            }

            IsModelInstalled = true;
            DownloadStatus = "Download complete ✓";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
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
}
