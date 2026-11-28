using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.LocalHost;

/// <summary>
/// Facade coordinating cached multi-host discovery (15 s TTL), delegated model downloads into shared local libraries
/// (Bionic, LM Studio, Ollama), optional per-host API token storage, and user-initiated host launch actions.
/// </summary>
public sealed class LocalHostCoordinator
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    private readonly LocalHostRegistry _registry;
    private readonly ILocalHostEnvironmentProbe _environment;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, LocalHostDownloadJob> _downloads = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<LocalHostInstance> _hosts = [];
    private DateTimeOffset _lastRefreshed = DateTimeOffset.MinValue;
    private Task<IReadOnlyList<LocalHostInstance>>? _inflightRefresh;

    public LocalHostCoordinator(
        LocalHostRegistry registry,
        ILocalHostEnvironmentProbe environment,
        ISettingsService settings,
        ISecretStore secrets,
        TimeProvider? time = null)
    {
        _registry = registry;
        _environment = environment;
        _settings = settings;
        _secrets = secrets;
        _time = time ?? TimeProvider.System;
    }

    public event Action? Changed;

    public IReadOnlyList<LocalHostInstance> Hosts
    {
        get
        {
            lock (_gate)
                return _hosts;
        }
    }

    public IReadOnlyList<LocalHostDownloadJob> ActiveDownloads
    {
        get
        {
            lock (_gate)
                return _downloads.Values.ToArray();
        }
    }

    public bool IsRefreshing
    {
        get
        {
            lock (_gate)
                return _inflightRefresh is { IsCompleted: false };
        }
    }

    public LocalHostInstance? FindHost(string? hostId)
    {
        if (string.IsNullOrWhiteSpace(hostId))
            return null;
        lock (_gate)
            return _hosts.FirstOrDefault(h => string.Equals(h.HostId, hostId, StringComparison.OrdinalIgnoreCase));
    }

    public LocalHostModelInfo? FindModel(string? hostId, string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey) || FindHost(hostId) is not { } host)
            return null;
        return host.Models.FirstOrDefault(m => string.Equals(m.Key, modelKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the cached discovery snapshot or triggers a coalesced background refresh when expired or forced.</summary>
    public Task<IReadOnlyList<LocalHostInstance>> RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        Task<IReadOnlyList<LocalHostInstance>> task;
        lock (_gate)
        {
            if (!force && _lastRefreshed != DateTimeOffset.MinValue && (_time.GetUtcNow() - _lastRefreshed) < CacheTtl)
                return Task.FromResult(_hosts);

            if (_inflightRefresh is { IsCompleted: false } existing)
                task = existing;
            else
                task = _inflightRefresh = RunRefreshAsync();
        }

        return task.WaitAsync(ct);
    }

    public async Task<bool> StartDownloadAsync(string hostId, ModelStage stage, string catalogModelId, HardwareProfile hardware, CancellationToken ct = default)
    {
        var host = FindHost(hostId);
        if (host is null || host.Status != LocalHostStatus.Running || !host.SupportsDownload)
            return false;

        var downloader = _registry.GetDownloader(host.Kind);
        if (downloader is null)
            return false;

        var recommendation = LocalHostModelMatcher.RecommendForMissing(stage, hardware)
            .FirstOrDefault(r => string.Equals(r.CatalogModel.Id, catalogModelId, StringComparison.Ordinal));
        if (recommendation is null)
            return false;

        var key = DownloadKey(hostId, catalogModelId);
        var token = await SafeGetSecretAsync(LocalHostSettings.TokenSecretIdFor(hostId), ct).ConfigureAwait(false);
        try
        {
            var job = await downloader.StartDownloadAsync(host, recommendation, token, ct).ConfigureAwait(false);
            lock (_gate)
                _downloads[key] = job;
            Changed?.Invoke();

            if (job.IsCompleted)
            {
                _ = RefreshAsync(force: true);
                return true;
            }

            if (job.IsRunning && !string.IsNullOrWhiteSpace(job.JobId))
                _ = PollDownloadAsync(downloader, host, job, token);

            return true;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _downloads[key] = new LocalHostDownloadJob(
                    JobId: "",
                    HostId: hostId,
                    ModelId: catalogModelId,
                    Status: "failed",
                    Error: ex.Message);
            }
            Changed?.Invoke();
            return false;
        }
    }

    public async Task SaveTokenAsync(string hostId, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostId))
            return;
        var secretId = LocalHostSettings.TokenSecretIdFor(hostId);
        if (string.IsNullOrWhiteSpace(token))
            await _secrets.DeleteSecretAsync(secretId, ct).ConfigureAwait(false);
        else
            await _secrets.SetSecretAsync(secretId, token.Trim(), ct).ConfigureAwait(false);

        await RefreshAsync(force: true, ct).ConfigureAwait(false);
    }

    public bool TryOpenHostApp(string hostId) =>
        FindHost(hostId) is { } host && _environment.TryOpenHostApp(host);

    public async Task<bool> TryStartLocalServerAsync(string hostId, CancellationToken ct = default)
    {
        if (FindHost(hostId) is not { } host)
            return false;

        var started = await _environment.TryStartLocalServerAsync(host, ct).ConfigureAwait(false);
        if (started)
            await RefreshAsync(force: true, ct).ConfigureAwait(false);
        return started;
    }

    private async Task<IReadOnlyList<LocalHostInstance>> RunRefreshAsync()
    {
        try
        {
            var targets = await _environment.BuildTargetsAsync(_settings.Current.LocalHost, _secrets, CancellationToken.None).ConfigureAwait(false);
            var discovered = await _registry.DiscoverAsync(targets, CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                _hosts = discovered;
                _lastRefreshed = _time.GetUtcNow();
            }
            Changed?.Invoke();
            return discovered;
        }
        finally
        {
            lock (_gate)
                _inflightRefresh = null;
        }
    }

    private async Task PollDownloadAsync(
        ILocalHostModelDownloader downloader,
        LocalHostInstance host,
        LocalHostDownloadJob initial,
        string? bearerToken)
    {
        var key = DownloadKey(host.HostId, initial.ModelId);
        var current = initial;
        for (var i = 0; i < 3600 && current.IsRunning; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            try
            {
                current = await downloader.GetDownloadStatusAsync(host, current.JobId, current.ModelId, bearerToken, CancellationToken.None).ConfigureAwait(false);
                lock (_gate)
                    _downloads[key] = current;
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _downloads[key] = current with { Status = "failed", Error = ex.Message };
                Changed?.Invoke();
                return;
            }
        }

        if (current.IsCompleted)
        {
            lock (_gate)
                _downloads.Remove(key);
            await RefreshAsync(force: true).ConfigureAwait(false);
        }
    }

    private async Task<string?> SafeGetSecretAsync(string secretId, CancellationToken ct)
    {
        try
        {
            return await _secrets.GetSecretAsync(secretId, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string DownloadKey(string hostId, string modelId) => $"{hostId}::{modelId}";
}
