using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost.Providers;

/// <summary>
/// Provider strategy for the LM Studio family: Bionic (default port 1235), LM Studio (default port 1234), and
/// headless <c>llmster</c>. Prefers the native v1 REST API (<c>/api/v1/models</c>, <c>/api/v1/chat</c>,
/// <c>/api/v1/models/load</c>, <c>/api/v1/models/download</c>) which naturally excludes Bionic+ cloud models,
/// internal ASR/TTS models, and Bionic's internal RPC port (<c>http-server.json</c>).
/// </summary>
public sealed class LmStudioFamilyProvider(HttpClient http) : ILocalHostProvider, ILocalHostModelDownloader
{
    public bool CanHandle(LocalHostKind kind) => kind is LocalHostKind.Bionic or LocalHostKind.LmStudio;

    public bool CanDownloadFor(LocalHostKind kind) => kind is LocalHostKind.Bionic or LocalHostKind.LmStudio;

    public async Task<LocalHostInstance> ProbeAsync(LocalHostProbeTarget target, CancellationToken ct)
    {
        // 1. Native v1 REST API (Bionic 1.2+ / LM Studio 0.4.0+): authoritative local inventory.
        var v1Uri = new Uri(target.BaseUri, "/api/v1/models");
        using var v1Resp = await SendGetAsync(v1Uri, target.BearerToken, ct).ConfigureAwait(false);
        if (v1Resp.StatusCode == HttpStatusCode.Unauthorized)
            return AuthRequired(target, LocalHostApiTier.NativeV1);
        if (v1Resp.IsSuccessStatusCode)
        {
            var body = await v1Resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (TryParseV1Models(body, out var models))
            {
                return new LocalHostInstance(
                    target.HostId,
                    target.Kind,
                    target.DisplayName,
                    target.BaseUri,
                    LocalHostStatus.Running,
                    LocalHostApiTier.NativeV1,
                    models,
                    SupportsDownload: true,
                    SupportsStartServer: target.CanStartServer,
                    SupportsOpenApp: target.CanOpenApp,
                    InstallPath: target.InstallPath);
            }
        }

        // 2. Legacy v0 REST API (LM Studio 0.3.x).
        var v0Uri = new Uri(target.BaseUri, "/api/v0/models");
        using var v0Resp = await SendGetAsync(v0Uri, target.BearerToken, ct).ConfigureAwait(false);
        if (v0Resp.StatusCode == HttpStatusCode.Unauthorized)
            return AuthRequired(target, LocalHostApiTier.LegacyV0);
        if (v0Resp.IsSuccessStatusCode)
        {
            var body = await v0Resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (TryParseV0Models(body, out var models))
            {
                return new LocalHostInstance(
                    target.HostId,
                    target.Kind,
                    target.DisplayName,
                    target.BaseUri,
                    LocalHostStatus.Running,
                    LocalHostApiTier.LegacyV0,
                    models,
                    SupportsDownload: false,
                    SupportsStartServer: target.CanStartServer,
                    SupportsOpenApp: target.CanOpenApp,
                    InstallPath: target.InstallPath);
            }
        }

        // 3. OpenAI-compatible fallback ONLY when /v1/models responds 200 (never accept /lmstudio-greeting alone,
        // because Bionic's internal RPC port answers /lmstudio-greeting with 200 but 404s all model endpoints).
        var openaiUri = new Uri(target.BaseUri, "/v1/models");
        using var openaiResp = await SendGetAsync(openaiUri, target.BearerToken, ct).ConfigureAwait(false);
        if (openaiResp.StatusCode == HttpStatusCode.Unauthorized)
            return AuthRequired(target, LocalHostApiTier.OpenAiCompat);
        if (openaiResp.IsSuccessStatusCode)
        {
            var body = await openaiResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (TryParseOpenAiModels(body, out var models))
            {
                return new LocalHostInstance(
                    target.HostId,
                    target.Kind,
                    target.DisplayName,
                    target.BaseUri,
                    LocalHostStatus.Running,
                    LocalHostApiTier.OpenAiCompat,
                    models,
                    SupportsDownload: false,
                    SupportsStartServer: target.CanStartServer,
                    SupportsOpenApp: target.CanOpenApp,
                    InstallPath: target.InstallPath);
            }
        }

        return new LocalHostInstance(
            target.HostId,
            target.Kind,
            target.DisplayName,
            target.BaseUri,
            target.IsInstalledHint ? LocalHostStatus.InstalledNotServing : LocalHostStatus.NotInstalled,
            LocalHostApiTier.NativeV1,
            [],
            SupportsDownload: false,
            SupportsStartServer: target.CanStartServer,
            SupportsOpenApp: target.CanOpenApp,
            InstallPath: target.InstallPath);
    }

    public IChatCompletionClient CreateChatClient(LocalHostRoute route, HttpClient chatHttp, ModelUsage? usage = null) =>
        new LmStudioChatClient(chatHttp, route);

    /// <summary>
    /// Polite pre-warm on composer summon: queries <c>/api/v1/models</c> first and only invokes <c>/api/v1/models/load</c>
    /// when the host currently has NO loaded model at all, so we never trigger Auto-Evict against an active Bionic session.
    /// </summary>
    public async Task TryWarmLoadAsync(LocalHostRoute route, CancellationToken ct)
    {
        if (route.ApiTier != LocalHostApiTier.NativeV1)
            return;

        using var probeResp = await SendGetAsync(route.ResolveEndpoint("/api/v1/models"), route.BearerToken, ct).ConfigureAwait(false);
        if (!probeResp.IsSuccessStatusCode)
            return;

        var body = await probeResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!TryParseV1Models(body, out var models))
            return;

        var target = models.FirstOrDefault(m => string.Equals(m.Key, route.ModelKey, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.IsLoaded)
            return;

        // Shared-host courtesy: if another model is currently loaded (e.g. Bionic's active session), defer until actual inference.
        if (models.Any(m => m.IsLoaded))
            return;

        var payload = new Dictionary<string, object>
        {
            ["model"] = route.ModelKey
        };
        using var req = CreateJsonPost(route.ResolveEndpoint("/api/v1/models/load"), payload, route.BearerToken);
        using var loadResp = await http.SendAsync(req, ct).ConfigureAwait(false);
        _ = loadResp.StatusCode;
    }

    public async Task<LocalHostDownloadJob> StartDownloadAsync(
        LocalHostInstance host,
        LocalHostRecommendation recommendation,
        string? bearerToken,
        CancellationToken ct)
    {
        if (host.BaseUri is null || host.ApiTier != LocalHostApiTier.NativeV1)
        {
            throw new LocalHostUnavailableException(
                LocalHostFailureKind.HostError,
                host.HostId,
                host.DisplayName,
                $"{host.DisplayName} does not support delegated v1 downloads.");
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = recommendation.HuggingFaceRepo
        };
        if (!string.IsNullOrWhiteSpace(recommendation.RecommendedQuantization))
            payload["quantization"] = recommendation.RecommendedQuantization;

        using var req = CreateJsonPost(new Uri(host.BaseUri, "/api/v1/models/download"), payload, bearerToken);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new LocalHostUnavailableException(
                LocalHostFailureKind.AuthRequired, host.HostId, host.DisplayName,
                $"{host.DisplayName} requires an API token to download models.");
        }

        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseDownloadJob(body, host.HostId, recommendation.CatalogModel.Id);
    }

    public async Task<LocalHostDownloadJob> GetDownloadStatusAsync(
        LocalHostInstance host,
        string jobId,
        string modelId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (host.BaseUri is null)
            throw new InvalidOperationException("Host has no base URI.");

        var uri = new Uri(host.BaseUri, $"/api/v1/models/download/status/{Uri.EscapeDataString(jobId)}");
        using var resp = await SendGetAsync(uri, bearerToken, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseDownloadJob(body, host.HostId, modelId, fallbackJobId: jobId);
    }

    private async Task<HttpResponseMessage> SendGetAsync(Uri uri, string? bearerToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateJsonPost(Uri uri, object payload, string? bearerToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(bearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return req;
    }

    private static LocalHostInstance AuthRequired(LocalHostProbeTarget target, LocalHostApiTier tier) => new(
        target.HostId,
        target.Kind,
        target.DisplayName,
        target.BaseUri,
        LocalHostStatus.AuthRequired,
        tier,
        [],
        SupportsDownload: false,
        SupportsStartServer: target.CanStartServer,
        SupportsOpenApp: target.CanOpenApp,
        InstallPath: target.InstallPath);

    internal static bool TryParseV1Models(string json, out IReadOnlyList<LocalHostModelInfo> models)
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
                var type = ReadString(item, "type");
                if (!string.Equals(type, "llm", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = ReadString(item, "key");
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var displayName = ReadString(item, "display_name") ?? key;
                var publisher = ReadString(item, "publisher");
                var arch = ReadString(item, "architecture");
                var format = ReadString(item, "format");
                var paramsString = ReadString(item, "params_string");
                var sizeBytes = ReadLong(item, "size_bytes");
                var maxCtx = ReadNullableInt(item, "max_context_length");

                string? quantName = null;
                if (item.TryGetProperty("quantization", out var quant) && quant.ValueKind == JsonValueKind.Object)
                    quantName = ReadString(quant, "name");

                var isLoaded = false;
                string? loadedEngine = null;
                int? loadedCtx = null;
                int? parallel = null;
                if (item.TryGetProperty("loaded_instances", out var loadedArr)
                    && loadedArr.ValueKind == JsonValueKind.Array
                    && loadedArr.GetArrayLength() > 0)
                {
                    isLoaded = true;
                    var first = loadedArr[0];
                    loadedEngine = ReadString(first, "engine");
                    if (first.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
                    {
                        loadedCtx = ReadNullableInt(cfg, "context_length");
                        parallel = ReadNullableInt(cfg, "parallel");
                    }
                }

                LocalHostReasoningCapability? reasoning = null;
                if (item.TryGetProperty("capabilities", out var caps)
                    && caps.ValueKind == JsonValueKind.Object
                    && caps.TryGetProperty("reasoning", out var rObj)
                    && rObj.ValueKind == JsonValueKind.Object)
                {
                    var allowed = new List<string>();
                    if (rObj.TryGetProperty("allowed_options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var opt in opts.EnumerateArray())
                        {
                            if (opt.ValueKind == JsonValueKind.String && opt.GetString() is { Length: > 0 } s)
                                allowed.Add(s);
                        }
                    }

                    reasoning = new LocalHostReasoningCapability(allowed, ReadString(rObj, "default"));
                }

                list.Add(new LocalHostModelInfo(
                    key,
                    displayName,
                    publisher,
                    arch,
                    format,
                    quantName,
                    paramsString,
                    sizeBytes,
                    maxCtx,
                    isLoaded,
                    loadedEngine,
                    loadedCtx,
                    parallel,
                    reasoning));
            }

            models = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParseV0Models(string json, out IReadOnlyList<LocalHostModelInfo> models)
    {
        models = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return false;

            var list = new List<LocalHostModelInfo>();
            foreach (var item in arr.EnumerateArray())
            {
                var type = ReadString(item, "type");
                if (type is not null
                    && !string.Equals(type, "llm", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "vlm", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = ReadString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var state = ReadString(item, "state");
                var isLoaded = string.Equals(state, "loaded", StringComparison.OrdinalIgnoreCase);
                list.Add(new LocalHostModelInfo(
                    id,
                    id,
                    Publisher: ReadString(item, "publisher"),
                    Architecture: ReadString(item, "arch"),
                    Quantization: ReadString(item, "quantization"),
                    MaxContextLength: ReadNullableInt(item, "max_context_length"),
                    IsLoaded: isLoaded,
                    LoadedContextLength: ReadNullableInt(item, "loaded_context_length")));
            }

            models = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryParseOpenAiModels(string json, out IReadOnlyList<LocalHostModelInfo> models)
    {
        models = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return false;

            var list = new List<LocalHostModelInfo>();
            foreach (var item in arr.EnumerateArray())
            {
                var id = ReadString(item, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    list.Add(new LocalHostModelInfo(id, id));
            }

            models = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static LocalHostDownloadJob ParseDownloadJob(string json, string hostId, string modelId, string? fallbackJobId = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var jobId = ReadString(root, "job_id") ?? fallbackJobId ?? "";
        var status = ReadString(root, "status") ?? "downloading";
        var downloaded = ReadLong(root, "downloaded_bytes");
        var total = ReadLong(root, "total_size_bytes");
        double? bps = root.TryGetProperty("bytes_per_second", out var bpsEl) && bpsEl.ValueKind == JsonValueKind.Number
            ? bpsEl.GetDouble()
            : null;
        var error = ReadString(root, "error");
        return new LocalHostDownloadJob(jobId, hostId, modelId, status, downloaded, total, bps, error);
    }

    private static string? ReadString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long ReadLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : 0;

    private static int? ReadNullableInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;
}
