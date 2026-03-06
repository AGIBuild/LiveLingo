using Microsoft.Extensions.Logging;
using Velopack;

namespace LiveLingo.App.Services.Update;

public sealed class VelopackUpdateService : IUpdateService
{
    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _updateInfo;

    public VelopackUpdateService(string updateUrl, ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;
        _updateManager = new UpdateManager(updateUrl);
    }

    public bool IsUpdateAvailable => _updateInfo is not null;
    public string? AvailableVersion => _updateInfo?.TargetFullRelease?.Version?.ToString();

    public async Task<bool> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            _updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (_updateInfo is not null)
                _logger.LogInformation("Update available: {Version}", AvailableVersion);
            return _updateInfo is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            _updateInfo = null;
            return false;
        }
    }

    public async Task DownloadAndApplyAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_updateInfo is null)
            throw new InvalidOperationException("No update available. Call CheckForUpdateAsync first.");

        _logger.LogInformation("Downloading update {Version}...", AvailableVersion);
        await _updateManager.DownloadUpdatesAsync(_updateInfo, progress: percentComplete =>
        {
            progress?.Report(percentComplete);
        });

        _logger.LogInformation("Applying update and restarting...");
        _updateManager.ApplyUpdatesAndRestart(_updateInfo);
    }
}
