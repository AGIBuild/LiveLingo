namespace LiveLingo.App.Services.Update;

public interface IUpdateService
{
    bool IsUpdateAvailable { get; }
    string? AvailableVersion { get; }
    Task<bool> CheckForUpdateAsync(CancellationToken ct = default);
    Task DownloadAndApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default);
}
