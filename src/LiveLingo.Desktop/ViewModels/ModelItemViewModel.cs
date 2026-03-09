using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels;

public partial class ModelItemViewModel : ObservableObject
{
    private readonly ModelDescriptor _descriptor;
    private readonly IModelManager _modelManager;
    private readonly ILocalizationService? _loc;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _downloadCts;

    public string Id => _descriptor.Id;
    public string DisplayName => _descriptor.DisplayName;
    public string TypeLabel => _descriptor.Type switch
    {
        ModelType.Translation => L("model.type.translation", "Translation"),
        ModelType.LanguageDetection => L("model.type.languageDetection", "Language Detection"),
        ModelType.PostProcessing => L("model.type.postProcessing", "Post-Processing"),
        _ => _descriptor.Type.ToString()
    };
    public string SizeText => FormatBytes(_descriptor.SizeBytes);
    public string DownloadButtonLabel => L("settings.models.download", "Download");
    public string CancelButtonLabel => L("settings.models.cancel", "Cancel");
    public string InstalledLabel => L("settings.models.installed", "✓ Installed");
    public string DeleteButtonLabel => L("settings.models.delete", "Delete");

    public ModelItemViewModel(
        ModelDescriptor descriptor,
        IModelManager modelManager,
        bool isInstalled,
        ILocalizationService? localizationService = null)
    {
        _descriptor = descriptor;
        _modelManager = modelManager;
        _isInstalled = isInstalled;
        _loc = localizationService;
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading || IsInstalled) return;

        IsDownloading = true;
        ErrorMessage = null;
        DownloadProgress = 0;

        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<ModelDownloadProgress>(p =>
            DownloadProgress = p.Percentage);

        try
        {
            await _modelManager.EnsureModelAsync(_descriptor, progress, _downloadCts.Token);
            IsInstalled = true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = L("wizard.download.cancelled", "Cancelled");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
    private async Task DeleteAsync()
    {
        if (!IsInstalled) return;

        try
        {
            await _modelManager.DeleteModelAsync(Id);
            IsInstalled = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_048_576 => $"{bytes / 1024.0:F0} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F0} MB",
        _ => $"{bytes / 1_073_741_824.0:F1} GB"
    };

    public static ObservableCollection<ModelItemViewModel> CreateAll(
        IModelManager modelManager,
        ILocalizationService? localizationService = null)
    {
        var installed = modelManager.ListInstalled();
        var installedIds = new HashSet<string>(installed.Select(m => m.Id));

        return new ObservableCollection<ModelItemViewModel>(
            ModelRegistry.AllModels.Select(d =>
                new ModelItemViewModel(d, modelManager, installedIds.Contains(d.Id), localizationService)));
    }

    private string L(string key, string fallback) => _loc?.T(key) ?? fallback;
}
