using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels;

/// <summary>
/// Presentation model for a single row in Settings → Models.
/// Download lifecycle is owned by the singleton <see cref="IModelDownloadCoordinator"/>;
/// this VM is a stateless observer that mirrors the coordinator's latest
/// <see cref="ModelDownloadState"/>. Closing and reopening the Settings window
/// therefore no longer cancels an in-flight download.
/// </summary>
public partial class ModelItemViewModel : ObservableObject, IDisposable
{
    private readonly ModelDescriptor _descriptor;
    private readonly IModelManager _modelManager;
    private readonly IModelDownloadCoordinator _coordinator;
    private readonly ILocalizationService? _loc;
    private readonly IPlatformServices? _platform;
    private readonly SynchronizationContext? _uiContext;
    private readonly Action<ModelDownloadState> _onStateChanged;
    private bool _disposed;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>
    /// Download is only offered when the model is neither installed nor currently
    /// downloading; a separate Cancel button owns the in-progress state so the two
    /// actions never share the same slot in the card.
    /// </summary>
    public bool ShowDownloadButton => !IsInstalled && !IsDownloading;

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(ShowDownloadButton));
    partial void OnIsDownloadingChanged(bool value) => OnPropertyChanged(nameof(ShowDownloadButton));

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
    public string OpenOnHuggingFaceLabel => L("settings.models.openOnHuggingFace", "Open on Hugging Face");
    public bool ShowOpenOnHuggingFace =>
        _platform is not null && HuggingFaceWebUrls.TryGetModelCardUrl(_descriptor.DownloadUrl, out _);

    public ModelItemViewModel(
        ModelDescriptor descriptor,
        IModelManager modelManager,
        IModelDownloadCoordinator coordinator,
        ILocalizationService? localizationService = null,
        IPlatformServices? platformServices = null,
        SynchronizationContext? uiContext = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _loc = localizationService;
        _platform = platformServices;
        _uiContext = uiContext ?? SynchronizationContext.Current;

        ApplyState(_coordinator.GetState(_descriptor.Id));

        _onStateChanged = OnCoordinatorStateChanged;
        _coordinator.StateChanged += _onStateChanged;
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsDownloading || IsInstalled) return;
        ErrorMessage = null;
        await _coordinator.StartAsync(_descriptor).ConfigureAwait(false);
    }

    [RelayCommand]
    private void CancelDownload() => _coordinator.Cancel(_descriptor.Id);

    [RelayCommand]
    private void OpenOnHuggingFace()
    {
        if (_platform is null) return;
        if (!HuggingFaceWebUrls.TryGetModelCardUrl(_descriptor.DownloadUrl, out var url)) return;
        _platform.OpenUrl(url);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsInstalled) return;

        try
        {
            await _modelManager.DeleteModelAsync(Id);
            _coordinator.NotifyDeleted(Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void OnCoordinatorStateChanged(ModelDownloadState state)
    {
        if (!string.Equals(state.ModelId, _descriptor.Id, StringComparison.Ordinal)) return;

        if (_uiContext is null)
        {
            ApplyState(state);
            return;
        }

        _uiContext.Post(_ => ApplyState(state), null);
    }

    private void ApplyState(ModelDownloadState state)
    {
        if (_disposed) return;

        IsInstalled = state.Status == ModelDownloadStatus.Installed;
        IsDownloading = state.Status == ModelDownloadStatus.Downloading;
        DownloadProgress = state.Percentage;
        ErrorMessage = state.Status switch
        {
            ModelDownloadStatus.Cancelled => L("wizard.download.cancelled", "Cancelled"),
            ModelDownloadStatus.Failed when state.ErrorMessage == ModelDownloadErrorCodes.HuggingFaceAuthorization =>
                L("settings.models.errorHuggingFaceAuth",
                  "Access denied by Hugging Face. Add a read token under Advanced → Access token, click Save, then retry."),
            ModelDownloadStatus.Failed => state.ErrorMessage,
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StateChanged -= _onStateChanged;
    }

    /// <summary>
    /// Public so that other view-models (e.g. SettingsViewModel's Speech tab) can format STT model
    /// sizes the same way the Models list does, without duplicating the formula.
    /// </summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1_048_576 => $"{bytes / 1024.0:F0} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F0} MB",
        _ => $"{bytes / 1_073_741_824.0:F1} GB"
    };

    public static ObservableCollection<ModelItemViewModel> CreateAll(
        IModelManager modelManager,
        IModelDownloadCoordinator coordinator,
        ILocalizationService? localizationService = null,
        IPlatformServices? platformServices = null,
        SynchronizationContext? uiContext = null)
    {
        return new ObservableCollection<ModelItemViewModel>(
            ModelRegistry.AllModels.Select(d =>
                new ModelItemViewModel(
                    d,
                    modelManager,
                    coordinator,
                    localizationService,
                    platformServices,
                    uiContext)));
    }

    private string L(string key, string fallback)
        => _loc is not null && _loc.TryT(key, out var value) ? value : fallback;
}
