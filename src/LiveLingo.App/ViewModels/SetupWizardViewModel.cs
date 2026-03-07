using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Services.Configuration;
using LiveLingo.Core.Models;

namespace LiveLingo.App.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _sourceLanguage = "zh";
    [ObservableProperty] private string _targetLanguage = "en";
    [ObservableProperty] private string _overlayHotkey = "Ctrl+Alt+T";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _downloadStatus;
    [ObservableProperty] private bool _isModelInstalled;

    public int TotalSteps { get; }
    public int StartStep { get; }
    public int DisplayStep => CurrentStep + 1;
    public bool CanGoBack => CurrentStep > StartStep;
    public bool CanGoNext => CurrentStep < TotalSteps - 1;
    public bool IsLastStep => CurrentStep == TotalSteps - 1;
    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;

    public event Action? RequestClose;

    public SetupWizardViewModel(ISettingsService settings, IModelManager? modelManager = null, int startStep = 0)
    {
        _settings = settings;
        _modelManager = modelManager;
        TotalSteps = 3;
        StartStep = startStep;
        _currentStep = startStep;

        if (_modelManager is not null)
        {
            var installed = _modelManager.ListInstalled();
            _isModelInstalled = installed.Any(m => m.Id == ModelRegistry.Qwen25_15B.Id);
        }
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

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            DownloadProgress = p.Percentage;
            DownloadStatus = $"Downloading… {p.Percentage:F0}%";
        });

        try
        {
            await _modelManager.EnsureModelAsync(ModelRegistry.Qwen25_15B, progress, _downloadCts.Token);
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

        RequestClose?.Invoke();
    }
}
