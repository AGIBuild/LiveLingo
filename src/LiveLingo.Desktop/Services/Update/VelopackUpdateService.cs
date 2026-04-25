using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace LiveLingo.Desktop.Services.Update;

public sealed class VelopackUpdateService : IUpdateService
{
    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _updateInfo;

    public VelopackUpdateService(string updateUrl, ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;
        var source = IsGitHubUrl(updateUrl)
            ? new GithubSource(updateUrl, accessToken: null, prerelease: false)
            : (IUpdateSource)new SimpleWebSource(updateUrl);
        _updateManager = new UpdateManager(source);
    }

    public bool IsInstalled => _updateManager.IsInstalled;
    public bool IsUpdateAvailable => _updateInfo is not null;
    public string? AvailableVersion => _updateInfo?.TargetFullRelease?.Version?.ToString();

    public async Task<bool> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!_updateManager.IsInstalled)
        {
            _logger.LogDebug("Skipping update check — app is not installed via Velopack");
            return false;
        }

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
            throw;
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

    private static bool IsGitHubUrl(string url) =>
        url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);
}
