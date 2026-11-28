using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost;

/// <summary>
/// Strategy contract for one local model host protocol family (for example Bionic/LM Studio, Ollama, or generic OpenAI-compatible local servers).
/// </summary>
public interface ILocalHostProvider
{
    bool CanHandle(LocalHostKind kind);

    /// <summary>Probes one loopback target within the caller's timeout and returns its current status and normalized model inventory.</summary>
    Task<LocalHostInstance> ProbeAsync(LocalHostProbeTarget target, CancellationToken ct);

    /// <summary>Creates a chat completion client speaking the host's optimal protocol.</summary>
    IChatCompletionClient CreateChatClient(LocalHostRoute route, HttpClient http, ModelUsage? usage = null);

    /// <summary>
    /// Best-effort polite pre-warming when the composer opens: must never evict another active model on a shared host.
    /// </summary>
    Task TryWarmLoadAsync(LocalHostRoute route, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Optional capability for local hosts that support delegated model downloads directly into their shared library.</summary>
public interface ILocalHostModelDownloader
{
    bool CanDownloadFor(LocalHostKind kind);

    Task<LocalHostDownloadJob> StartDownloadAsync(
        LocalHostInstance host,
        LocalHostRecommendation recommendation,
        string? bearerToken,
        CancellationToken ct);

    Task<LocalHostDownloadJob> GetDownloadStatusAsync(
        LocalHostInstance host,
        string jobId,
        string modelId,
        string? bearerToken,
        CancellationToken ct);
}

/// <summary>
/// Coordinates parallel auto-discovery across all registered <see cref="ILocalHostProvider"/> strategies, deduplicates
/// shared endpoints (for example Bionic delegating to LM Studio on the same port), and dispatches chat/download operations.
/// </summary>
public sealed class LocalHostRegistry
{
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(800);

    private readonly IReadOnlyList<ILocalHostProvider> _providers;
    private readonly IReadOnlyList<ILocalHostModelDownloader> _downloaders;
    private readonly TimeSpan _probeTimeout;

    public LocalHostRegistry(
        IEnumerable<ILocalHostProvider> providers,
        TimeSpan? probeTimeout = null)
    {
        _providers = providers.ToArray();
        _downloaders = _providers.OfType<ILocalHostModelDownloader>().ToArray();
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
    }

    public async Task<IReadOnlyList<LocalHostInstance>> DiscoverAsync(
        IReadOnlyList<LocalHostProbeTarget> targets,
        CancellationToken ct = default)
    {
        if (targets.Count == 0)
            return [];

        var probeTasks = targets.Select(target => ProbeOneSafeAsync(target, ct)).ToArray();
        var results = await Task.WhenAll(probeTasks).ConfigureAwait(false);

        // Deduplicate running hosts listening on the exact same loopback port, preferring more specific native providers.
        var byAuthority = new Dictionary<string, LocalHostInstance>(StringComparer.OrdinalIgnoreCase);
        var nonRunning = new List<LocalHostInstance>();

        foreach (var instance in results)
        {
            if (instance.Status is LocalHostStatus.Running or LocalHostStatus.AuthRequired && instance.BaseUri is not null)
            {
                var key = NormalizeLoopbackKey(instance.BaseUri);
                if (!byAuthority.TryGetValue(key, out var existing) || KindPriority(instance.Kind) < KindPriority(existing.Kind))
                    byAuthority[key] = instance;
            }
            else if (instance.Status == LocalHostStatus.InstalledNotServing)
            {
                nonRunning.Add(instance);
            }
        }

        // Drop InstalledNotServing placeholders whose HostId already resolved to a Running/AuthRequired instance.
        var runningIds = byAuthority.Values.Select(i => i.HostId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = byAuthority.Values
            .Concat(nonRunning.Where(item => !runningIds.Contains(item.HostId)))
            .OrderBy(i => StatusPriority(i.Status))
            .ThenByDescending(i => i.HasLoadedModels)
            .ThenBy(i => KindPriority(i.Kind))
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged;
    }

    public IChatCompletionClient CreateChatClient(LocalHostRoute route, HttpClient http, ModelUsage? usage = null)
    {
        var provider = ResolveProvider(route.Kind);
        return provider.CreateChatClient(route, http, usage);
    }

    public Task TryWarmLoadAsync(LocalHostRoute route, CancellationToken ct) =>
        ResolveProvider(route.Kind).TryWarmLoadAsync(route, ct);

    public ILocalHostModelDownloader? GetDownloader(LocalHostKind kind) =>
        _downloaders.FirstOrDefault(d => d.CanDownloadFor(kind));

    private ILocalHostProvider ResolveProvider(LocalHostKind kind) =>
        _providers.FirstOrDefault(p => p.CanHandle(kind))
        ?? throw new InvalidOperationException($"No local host provider registered for '{kind}'.");

    private async Task<LocalHostInstance> ProbeOneSafeAsync(LocalHostProbeTarget target, CancellationToken ct)
    {
        if (!LocalHostRoute.TryParseLoopbackUri(target.BaseUri.ToString(), out _))
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, null,
                LocalHostStatus.NotInstalled, LocalHostApiTier.OpenAiCompat, []);
        }

        var provider = _providers.FirstOrDefault(p => p.CanHandle(target.Kind));
        if (provider is null)
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.NotInstalled, LocalHostApiTier.OpenAiCompat, []);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_probeTimeout);
        try
        {
            return await provider.ProbeAsync(target, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FallbackUnreachable(target);
        }
        catch (Exception)
        {
            return FallbackUnreachable(target);
        }
    }

    private static LocalHostInstance FallbackUnreachable(LocalHostProbeTarget target) => new(
        target.HostId,
        target.Kind,
        target.DisplayName,
        target.BaseUri,
        target.IsInstalledHint ? LocalHostStatus.InstalledNotServing : LocalHostStatus.NotInstalled,
        target.Kind switch
        {
            LocalHostKind.Bionic or LocalHostKind.LmStudio => LocalHostApiTier.NativeV1,
            LocalHostKind.Ollama => LocalHostApiTier.OllamaNative,
            _ => LocalHostApiTier.OpenAiCompat
        },
        [],
        SupportsDownload: false,
        SupportsStartServer: target.CanStartServer,
        SupportsOpenApp: target.CanOpenApp,
        InstallPath: target.InstallPath);

    private static string NormalizeLoopbackKey(Uri uri) => $"loopback:{uri.Port}";

    private static int StatusPriority(LocalHostStatus status) => status switch
    {
        LocalHostStatus.Running => 0,
        LocalHostStatus.AuthRequired => 1,
        LocalHostStatus.InstalledNotServing => 2,
        _ => 3
    };

    private static int KindPriority(LocalHostKind kind) => kind switch
    {
        LocalHostKind.Bionic => 0,
        LocalHostKind.LmStudio => 1,
        LocalHostKind.Ollama => 2,
        LocalHostKind.OpenAiLocal => 3,
        _ => 4
    };
}
