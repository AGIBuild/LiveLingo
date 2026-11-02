using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace LiveLingo.Desktop.Services.Update;

/// <summary>
/// Updates through Velopack from the GitHub releases of the repository (or any static web folder). An application that
/// was not installed by the Velopack installer (a development build, the MSI) has nothing to update.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly ILogger<VelopackUpdateService> _logger;
    private readonly UpdateManager _manager;
    private UpdateInfo? _ready;

    public VelopackUpdateService(string updateUrl, ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;
        IUpdateSource source = updateUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
            ? new GithubSource(updateUrl, accessToken: null, prerelease: false)
            : new SimpleWebSource(updateUrl);
        _manager = new UpdateManager(source);
    }

    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public async Task<bool> CheckAndDownloadAsync(CancellationToken ct = default)
    {
        if (_ready is not null)
            return true;

        if (!_manager.IsInstalled)
        {
            _logger.LogDebug("Skipping the update check: the application was not installed by the installer");
            return false;
        }

        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null)
            return false;

        _logger.LogInformation("Downloading update {Version}", update.TargetFullRelease.Version);
        await _manager.DownloadUpdatesAsync(update, cancelToken: ct).ConfigureAwait(false);
        _ready = update;
        return true;
    }

    public void ApplyAndRestart() => _manager.ApplyUpdatesAndRestart(Ready());

    public void ApplyOnExit() => _manager.WaitExitThenApplyUpdates(Ready(), silent: true, restart: false);

    private UpdateInfo Ready() => _ready ?? throw new InvalidOperationException("No update was downloaded.");
}
