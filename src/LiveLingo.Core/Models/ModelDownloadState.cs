namespace LiveLingo.Core.Models;

public enum ModelDownloadStatus
{
    Idle,
    Downloading,
    Installed,
    Cancelled,
    Failed
}

/// <summary>
/// Snapshot of a single model's download lifecycle as managed by
/// <see cref="IModelDownloadCoordinator"/>. The coordinator publishes a fresh
/// instance every time the status or progress changes, so ViewModels can simply
/// mirror fields instead of juggling their own <see cref="System.Threading.CancellationTokenSource"/>.
/// </summary>
public sealed record ModelDownloadState(
    string ModelId,
    ModelDownloadStatus Status,
    double Percentage,
    string? ErrorMessage)
{
    public bool IsDownloading => Status == ModelDownloadStatus.Downloading;
    public bool IsInstalled => Status == ModelDownloadStatus.Installed;
}
