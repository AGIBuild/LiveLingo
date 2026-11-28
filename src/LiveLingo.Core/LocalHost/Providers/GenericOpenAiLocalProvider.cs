using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost.Providers;

/// <summary>
/// Provider strategy for generic OpenAI-compatible loopback local servers (standalone <c>llama-server</c>,
/// <c>mlx_lm.server</c>, vLLM, Jan, LocalAI, or user-added custom loopback ports).
/// </summary>
public sealed class GenericOpenAiLocalProvider(HttpClient http) : ILocalHostProvider
{
    public bool CanHandle(LocalHostKind kind) => kind == LocalHostKind.OpenAiLocal;

    public async Task<LocalHostInstance> ProbeAsync(LocalHostProbeTarget target, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(target.BaseUri, "/v1/models"));
        if (!string.IsNullOrWhiteSpace(target.BearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.BearerToken);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.AuthRequired, LocalHostApiTier.OpenAiCompat, []);
        }

        if (!resp.IsSuccessStatusCode)
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.NotInstalled, LocalHostApiTier.OpenAiCompat, []);
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!TryParseOpenAiModels(body, out var rawModels))
        {
            return new LocalHostInstance(
                target.HostId, target.Kind, target.DisplayName, target.BaseUri,
                LocalHostStatus.NotInstalled, LocalHostApiTier.OpenAiCompat, []);
        }

        var propsCtx = await TryReadLlamaPropsContextAsync(target.BaseUri, target.BearerToken, ct).ConfigureAwait(false);
        var models = rawModels.Select(m => m with
        {
            MaxContextLength = m.MaxContextLength ?? propsCtx
        }).ToArray();

        return new LocalHostInstance(
            target.HostId,
            target.Kind,
            target.DisplayName,
            target.BaseUri,
            LocalHostStatus.Running,
            LocalHostApiTier.OpenAiCompat,
            models,
            SupportsDownload: false,
            SupportsStartServer: false,
            SupportsOpenApp: false);
    }

    public IChatCompletionClient CreateChatClient(LocalHostRoute route, HttpClient chatHttp, ModelUsage? usage = null) =>
        new OpenAiLocalChatClient(chatHttp, route);

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
                if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                    continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                // llama.cpp router mode (such as Llama.app) reports explicit per-model status ("loaded" / "unloaded");
                // single-model OpenAI servers omit status and are always loaded.
                var isLoaded = true;
                if (item.TryGetProperty("status", out var statusEl)
                    && statusEl.ValueKind == JsonValueKind.Object
                    && statusEl.TryGetProperty("value", out var statusVal)
                    && statusVal.ValueKind == JsonValueKind.String)
                {
                    isLoaded = string.Equals(statusVal.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
                }

                int? maxCtx = item.TryGetProperty("context_length", out var ctxEl)
                    && ctxEl.ValueKind == JsonValueKind.Number
                    && ctxEl.TryGetInt32(out var ctx)
                    ? ctx
                    : null;

                long sizeBytes = 0;
                string? quantization = null;
                if (item.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    if (metaEl.TryGetProperty("size", out var sizeEl)
                        && sizeEl.ValueKind == JsonValueKind.Number
                        && sizeEl.TryGetInt64(out var size))
                    {
                        sizeBytes = size;
                    }
                    if (metaEl.TryGetProperty("ftype", out var ftypeEl) && ftypeEl.ValueKind == JsonValueKind.String)
                        quantization = ftypeEl.GetString();
                }

                list.Add(new LocalHostModelInfo(
                    id,
                    id,
                    Quantization: quantization,
                    SizeBytes: sizeBytes,
                    MaxContextLength: maxCtx,
                    IsLoaded: isLoaded));
            }

            models = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<int?> TryReadLlamaPropsContextAsync(Uri baseUri, string? bearerToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/props"));
            req.Headers.ConnectionClose = true;
            if (!string.IsNullOrWhiteSpace(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("default_generation_settings", out var gen)
                && gen.ValueKind == JsonValueKind.Object
                && gen.TryGetProperty("n_ctx", out var ctx) && ctx.ValueKind == JsonValueKind.Number)
            {
                return ctx.GetInt32();
            }
        }
        catch
        {
            // Non-llama-server endpoints do not expose /props.
        }

        return null;
    }
}

/// <summary>Adapter wrapping <see cref="OpenAiChatClient"/> for local OpenAI-compatible servers with structured failure translation.</summary>
public sealed class OpenAiLocalChatClient(HttpClient http, LocalHostRoute route) : IChatCompletionClient
{
    private static readonly HashSet<string> SafeSamplingKeys = new(StringComparer.Ordinal)
    {
        "top_p", "seed"
    };

    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var extras = new Dictionary<string, object>();
        foreach (var (key, value) in request.Extra)
        {
            if (SafeSamplingKeys.Contains(key))
                extras[key] = value;
        }

        var inner = new OpenAiChatClient(
            http,
            route.ResolveEndpoint("/v1/chat/completions"),
            route.ModelKey,
            route.BearerToken,
            sendProviderExtras: extras.Count > 0);
        try
        {
            return await inner.CompleteAsync(request with { Extra = extras }, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            var kind = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LocalHostFailureKind.AuthRequired,
                HttpStatusCode.NotFound => LocalHostFailureKind.ModelNotFound,
                null => LocalHostFailureKind.ServerNotRunning,
                _ => LocalHostFailureKind.HostError
            };
            throw new LocalHostUnavailableException(
                kind, route.HostId, route.HostDisplayName,
                $"{route.HostDisplayName} request failed: {ex.Message}", ex);
        }
    }
}
