using System.Net;
using System.Text;
using System.Text.Json;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost.Providers;

/// <summary>
/// Provider strategy for Ollama (default <c>http://127.0.0.1:11434</c> or loopback <c>OLLAMA_HOST</c>).
/// Combines <c>GET /api/tags</c> (models on disk) with <c>GET /api/ps</c> (models currently resident in memory/VRAM)
/// to accurately highlight zero-extra-memory hot instances.
/// </summary>
public sealed class OllamaHostProvider(HttpClient http) : ILocalHostProvider, ILocalHostModelDownloader
{
    public bool CanHandle(LocalHostKind kind) => kind == LocalHostKind.Ollama;

    public bool CanDownloadFor(LocalHostKind kind) => kind == LocalHostKind.Ollama;

    public async Task<LocalHostInstance> ProbeAsync(LocalHostProbeTarget target, CancellationToken ct)
    {
        using var tagsReq = new HttpRequestMessage(HttpMethod.Get, new Uri(target.BaseUri, "/api/tags"));
        tagsReq.Headers.ConnectionClose = true;
        using var tagsResp = await http.SendAsync(tagsReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (tagsResp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.AuthRequired, LocalHostApiTier.OllamaNative, [],
                SupportsOpenApp: target.CanOpenApp, InstallPath: target.InstallPath);
        }

        if (!tagsResp.IsSuccessStatusCode)
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                target.IsInstalledHint ? LocalHostStatus.InstalledNotServing : LocalHostStatus.NotInstalled,
                LocalHostApiTier.OllamaNative, [],
                SupportsOpenApp: target.CanOpenApp, InstallPath: target.InstallPath);
        }

        var tagsJson = await tagsResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var loadedSet = await FetchLoadedModelsAsync(target.BaseUri, ct).ConfigureAwait(false);
        if (!TryParseOllamaTags(tagsJson, loadedSet, out var models))
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.NotInstalled, LocalHostApiTier.OllamaNative, []);
        }

        return new LocalHostInstance(
            target.HostId,
            target.Kind,
            target.DisplayName,
            target.BaseUri,
            LocalHostStatus.Running,
            LocalHostApiTier.OllamaNative,
            models,
            SupportsDownload: true,
            SupportsStartServer: false,
            SupportsOpenApp: target.CanOpenApp,
            InstallPath: target.InstallPath);
    }

    public IChatCompletionClient CreateChatClient(LocalHostRoute route, HttpClient chatHttp, ModelUsage? usage = null) =>
        new OllamaChatClient(chatHttp, route);

    /// <summary>Starts a non-streaming pull check or triggers Ollama pull and reports completion.</summary>
    public async Task<LocalHostDownloadJob> StartDownloadAsync(
        LocalHostInstance host,
        LocalHostRecommendation recommendation,
        string? bearerToken,
        CancellationToken ct)
    {
        if (host.BaseUri is null)
            throw new InvalidOperationException("Host has no base URI.");

        var payload = new { model = recommendation.OllamaTag, stream = false };
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(host.BaseUri, "/api/pull"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.ConnectionClose = true;
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return new LocalHostDownloadJob(
            JobId: $"ollama:{recommendation.OllamaTag}",
            HostId: host.HostId,
            ModelId: recommendation.CatalogModel.Id,
            Status: "completed",
            DownloadedBytes: recommendation.CatalogModel.SizeBytes,
            TotalBytes: recommendation.CatalogModel.SizeBytes);
    }

    public Task<LocalHostDownloadJob> GetDownloadStatusAsync(
        LocalHostInstance host,
        string jobId,
        string modelId,
        string? bearerToken,
        CancellationToken ct) =>
        Task.FromResult(new LocalHostDownloadJob(jobId, host.HostId, modelId, "completed"));

    private async Task<HashSet<string>> FetchLoadedModelsAsync(Uri baseUri, CancellationToken ct)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/ps"));
            req.Headers.ConnectionClose = true;
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return loaded;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        && nameEl.GetString() is { Length: > 0 } name)
                    {
                        loaded.Add(name);
                    }
                    else if (item.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                        && modelEl.GetString() is { Length: > 0 } model)
                    {
                        loaded.Add(model);
                    }
                }
            }
        }
        catch
        {
            // /api/ps is best-effort; /api/tags still gives the full disk list.
        }

        return loaded;
    }

    internal static bool TryParseOllamaTags(string json, HashSet<string> loadedSet, out IReadOnlyList<LocalHostModelInfo> models)
    {
        models = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return false;

            var list = new List<LocalHostModelInfo>();
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var size = item.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var bytes)
                    ? bytes
                    : 0;
                string? family = null;
                string? paramSize = null;
                string? quant = null;
                string? format = null;
                if (item.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
                {
                    family = ReadString(details, "family");
                    paramSize = ReadString(details, "parameter_size");
                    quant = ReadString(details, "quantization_level");
                    format = ReadString(details, "format");
                }

                var isLoaded = loadedSet.Contains(name);
                list.Add(new LocalHostModelInfo(
                    Key: name,
                    DisplayName: name,
                    Architecture: family,
                    Format: format,
                    Quantization: quant,
                    ParamsString: paramSize,
                    SizeBytes: size,
                    IsLoaded: isLoaded,
                    LoadedEngine: isLoaded ? "ollama" : null));
            }

            models = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
