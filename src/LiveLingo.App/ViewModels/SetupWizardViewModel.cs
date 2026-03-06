using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Services.Configuration;
using LiveLingo.Core.Models;

namespace LiveLingo.App.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager _modelManager;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _sourceLanguage = "zh";
    [ObservableProperty] private string _targetLanguage = "en";
    [ObservableProperty] private string _overlayHotkey = "Ctrl+Alt+T";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private bool _isCompleted;

    public int TotalSteps => 3;
    public bool CanGoBack => CurrentStep > 0;
    public bool CanGoNext => CurrentStep < TotalSteps - 1;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;

    public event Action? RequestClose;

    public SetupWizardViewModel(ISettingsService settings, IModelManager modelManager)
    {
        _settings = settings;
        _modelManager = modelManager;
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsLastStep));
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
    private async Task DownloadModel(CancellationToken ct)
    {
        if (IsDownloading) return;

        IsDownloading = true;
        DownloadStatus = "Finding model...";

        try
        {
            var descriptor = ModelRegistry.AllModels
                .FirstOrDefault(m => m.Type == ModelType.Translation);

            if (descriptor is null)
            {
                DownloadStatus = "No translation model available";
                return;
            }

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DownloadProgress = p.TotalBytes > 0
                    ? (double)p.BytesDownloaded / p.TotalBytes * 100
                    : 0;
                DownloadStatus = $"Downloading... {DownloadProgress:F0}%";
            });

            await _modelManager.EnsureModelAsync(descriptor, progress, ct);
            DownloadStatus = "Model ready";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "Download cancelled";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        _settings.Update(s => s with
        {
            Hotkeys = s.Hotkeys with { OverlayToggle = OverlayHotkey },
            Translation = s.Translation with
            {
                DefaultSourceLanguage = SourceLanguage,
                DefaultTargetLanguage = TargetLanguage,
                LanguagePairs = [new LanguagePair(SourceLanguage, TargetLanguage)]
            }
        });

        IsCompleted = true;
        RequestClose?.Invoke();
    }
}
