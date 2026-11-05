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
    private UpdateInfo? _found;
    private UpdateInfo? _ready;

    public VelopackUpdateService(string updateUrl, ILogger<VelopackUpdateService> logger)
    {
        _logger = logger;
        var github = updateUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);
        // GithubSource calls api.github.com, whose unauthenticated limit (60 requests an hour per IP) is easily exhausted
        // (403 rate limit exceeded). The latest release's assets, feed included, are served without that limit.
        IUpdateSource source = new SimpleWebSource(github
            ? updateUrl.TrimEnd('/') + "/releases/latest/download/"
            : updateUrl);
        DownloadPage = github ? updateUrl.TrimEnd('/') + "/releases/latest" : null;
        _manager = new UpdateManager(source);
    }

    public bool CanUpdate => _manager.IsInstalled;

    public string? DownloadPage { get; }

    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public async Task<string?> FindAsync(CancellationToken ct = default)
    {
        // The feed call has no cancellation of its own; a cancelled check is dropped when it returns.
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return _found?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAsync(Action<int> progress, CancellationToken ct = default)
    {
        var update = _found ?? throw new InvalidOperationException("No update was found.");
        _logger.LogInformation("Downloading update {Version}", update.TargetFullRelease.Version);
        await _manager.DownloadUpdatesAsync(update, progress, ct).ConfigureAwait(false);
        _ready = update;
    }

    public void ApplyAndRestart() => _manager.ApplyUpdatesAndRestart(Ready());

    public void ApplyOnExit() => _manager.WaitExitThenApplyUpdates(Ready(), silent: true, restart: false);

    private UpdateInfo Ready() => _ready ?? throw new InvalidOperationException("No update was downloaded.");
}
