using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost.Providers;

/// <summary>
/// Chat completion client for Bionic / LM Studio / <c>llmster</c>.
/// On <see cref="LocalHostApiTier.NativeV1"/> it calls <c>POST /api/v1/chat</c> with <c>store: false</c>,
/// conditionally passes <c>reasoning: "off"</c> only when the model's advertised capabilities include <c>"off"</c>,
/// and isolates <c>message.delta</c> from <c>reasoning.delta</c> SSE events.
/// On legacy v0 / OpenAI-compatible tiers it falls back to <c>/v1/chat/completions</c> with a 10-minute JIT <c>ttl</c>.
/// </summary>
public sealed class LmStudioChatClient(HttpClient http, LocalHostRoute route) : IChatCompletionClient
{
    private const int DefaultJitTtlSeconds = 600;

    private static readonly HashSet<string> NativeV1SamplingKeys = new(StringComparer.Ordinal)
    {
        "top_p", "top_k", "min_p", "repeat_penalty"
    };

    private static readonly HashSet<string> OpenAiCompatSamplingKeys = new(StringComparer.Ordinal)
    {
        "top_p", "top_k", "repeat_penalty", "seed"
    };

    public Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct) =>
        route.ApiTier == LocalHostApiTier.NativeV1
            ? CompleteNativeV1Async(request, ct)
            : CompleteOpenAiCompatAsync(request, ct);

    /// <summary>
    /// Concurrency-safe KV cache warm-up: only runs when the target model is already resident on the host with
    /// multiple parallel slots (for example Bionic's <c>omlx</c> with <c>parallel: 4</c>), so we never trigger a cold
    /// model load or contend with an active single-slot Bionic conversation during dictation.
    /// </summary>
    public async Task WarmPromptAsync(
        string systemPrompt,
        IReadOnlyDictionary<string, object>? extra = null,
        CancellationToken ct = default,
        string userPrefix = "目标：")
    {
        if (string.IsNullOrWhiteSpace(systemPrompt) || !route.IsLoaded || (route.ParallelSlots ?? 1) < 2)
            return;

        if (route.ApiTier != LocalHostApiTier.NativeV1)
            return;

        var payload = new Dictionary<string, object>
        {
            ["model"] = route.ModelKey,
            ["system_prompt"] = systemPrompt,
            ["input"] = userPrefix,
            ["stream"] = false,
            ["store"] = false,
            ["temperature"] = 0,
            ["max_output_tokens"] = 1
        };
        if (route.ReasoningOffSupported)
            payload["reasoning"] = "off";

        using var message = CreatePost(route.ResolveEndpoint("/api/v1/chat"), payload);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            _ = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<ChatCompletion> CompleteNativeV1Async(ChatRequest request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var payload = new Dictionary<string, object>
        {
            ["model"] = route.ModelKey,
            ["input"] = request.Prompt,
            ["stream"] = true,
            ["store"] = false,
            ["temperature"] = request.Temperature,
            ["max_output_tokens"] = request.MaxTokens
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["system_prompt"] = request.System;
        if (route.ReasoningOffSupported)
            payload["reasoning"] = "off";

        foreach (var (key, value) in request.Extra)
        {
            if (NativeV1SamplingKeys.Contains(key))
                payload[key] = value;
        }

        using var message = CreatePost(route.ResolveEndpoint("/api/v1/chat"), payload);
        using var response = await SendCheckedAsync(message, ct).ConfigureAwait(false);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));

        var text = new StringBuilder();
        double? firstVisibleMs = null;
        var outputTokens = 0;
        string? reportedModel = null;
        string? currentEvent = null;
        var completed = false;
        var loadingModel = false;
        double? lastLoadProgress = null;

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line[6..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data.Length == 0)
                continue;

            using var json = ParseEvent(data);
            var root = json.RootElement;
            var eventType = root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                ? typeEl.GetString()
                : currentEvent;

            switch (eventType)
            {
                case "chat.start":
                    if (root.TryGetProperty("model_instance_id", out var startModel) && startModel.ValueKind == JsonValueKind.String)
                        reportedModel = startModel.GetString();
                    break;

                case "model_load.start":
                    loadingModel = true;
                    break;

                case "model_load.progress":
                    loadingModel = true;
                    if (root.TryGetProperty("progress", out var prog) && prog.ValueKind == JsonValueKind.Number)
                        lastLoadProgress = prog.GetDouble();
                    break;

                case "model_load.end":
                    loadingModel = false;
                    break;

                case "message.delta":
                    loadingModel = false;
                    if (root.TryGetProperty("content", out var delta)
                        && delta.ValueKind == JsonValueKind.String
                        && delta.GetString() is { Length: > 0 } chunk)
                    {
                        text.Append(chunk);
                        if (!string.IsNullOrWhiteSpace(chunk))
                            firstVisibleMs ??= clock.Elapsed.TotalMilliseconds;
                        request.OnDelta?.Invoke(chunk);
                    }
                    break;

                case "chat.end":
                    loadingModel = false;
                    completed = true;
                    ParseChatEnd(root, ref reportedModel, ref outputTokens, text, ref firstVisibleMs, clock);
                    break;

                case "error":
                    var errMsg = ExtractErrorMessage(root);
                    throw ClassifyHostError(errMsg);
            }
        }

        if (!completed || firstVisibleMs is null)
        {
            if (loadingModel && text.Length == 0)
            {
                var pct = lastLoadProgress is { } p ? $" ({Math.Round(p * 100):0}%)" : "";
                throw new LocalHostUnavailableException(
                    LocalHostFailureKind.HostError,
                    route.HostId,
                    route.HostDisplayName,
                    $"Model '{route.ModelKey}' failed to load on {route.HostDisplayName}{pct}: stream closed before model finished loading.");
            }
            throw new InvalidDataException($"Incomplete {route.HostDisplayName} v1 chat stream: completed={completed}.");
        }

        return new ChatCompletion(
            text.ToString().Trim(),
            firstVisibleMs.Value,
            clock.Elapsed.TotalMilliseconds,
            outputTokens,
            reportedModel ?? route.ModelKey);
    }

    private async Task<ChatCompletion> CompleteOpenAiCompatAsync(ChatRequest request, CancellationToken ct)
    {
        var safeExtras = new Dictionary<string, object>
        {
            ["ttl"] = DefaultJitTtlSeconds
        };
        foreach (var (key, value) in request.Extra)
        {
            if (OpenAiCompatSamplingKeys.Contains(key))
                safeExtras[key] = value;
        }

        var openAi = new OpenAiChatClient(
            http,
            route.ResolveEndpoint("/v1/chat/completions"),
            route.ModelKey,
            route.BearerToken,
            sendProviderExtras: true,
            connectionClose: false);

        try
        {
            return await openAi.CompleteAsync(request with { Extra = safeExtras }, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateHttpException(ex);
        }
    }

    private async Task<HttpResponseMessage> SendCheckedAsync(HttpRequestMessage message, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalHostUnavailableException(
                LocalHostFailureKind.ServerNotRunning,
                route.HostId,
                route.HostDisplayName,
                $"Cannot reach {route.HostDisplayName} at {route.BaseUri}: {ex.Message}",
                ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        var statusCode = response.StatusCode;
        var detail = await SafeReadBodyAsync(response, ct).ConfigureAwait(false);
        response.Dispose();

        throw statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new LocalHostUnavailableException(
                LocalHostFailureKind.AuthRequired,
                route.HostId,
                route.HostDisplayName,
                $"{route.HostDisplayName} rejected the request (HTTP {(int)statusCode}): API token required or invalid."),
            HttpStatusCode.NotFound => new LocalHostUnavailableException(
                LocalHostFailureKind.ModelNotFound,
                route.HostId,
                route.HostDisplayName,
                $"Model '{route.ModelKey}' was not found on {route.HostDisplayName}."),
            _ => ClassifyHostError(detail, statusCode)
        };
    }

    private LocalHostUnavailableException TranslateHttpException(HttpRequestException ex) => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new LocalHostUnavailableException(
            LocalHostFailureKind.AuthRequired, route.HostId, route.HostDisplayName,
            $"{route.HostDisplayName} requires a valid API token.", ex),
        HttpStatusCode.NotFound => new LocalHostUnavailableException(
            LocalHostFailureKind.ModelNotFound, route.HostId, route.HostDisplayName,
            $"Model '{route.ModelKey}' was not found on {route.HostDisplayName}.", ex),
        null => new LocalHostUnavailableException(
            LocalHostFailureKind.ServerNotRunning, route.HostId, route.HostDisplayName,
            $"Cannot reach {route.HostDisplayName} at {route.BaseUri}.", ex),
        _ => new LocalHostUnavailableException(
            LocalHostFailureKind.HostError, route.HostId, route.HostDisplayName,
            $"{route.HostDisplayName} returned HTTP {(int)ex.StatusCode.Value}.", ex)
    };

    private LocalHostUnavailableException ClassifyHostError(string detail, HttpStatusCode? status = null)
    {
        var lower = detail.ToLowerInvariant();
        if (lower.Contains("not found", StringComparison.Ordinal)
            || lower.Contains("model_not_found", StringComparison.Ordinal)
            || lower.Contains("does not exist", StringComparison.Ordinal))
        {
            return new LocalHostUnavailableException(
                LocalHostFailureKind.ModelNotFound, route.HostId, route.HostDisplayName,
                $"Model '{route.ModelKey}' is not available on {route.HostDisplayName}: {detail}");
        }

        if (lower.Contains("context", StringComparison.Ordinal) && lower.Contains("length", StringComparison.Ordinal))
        {
            return new LocalHostUnavailableException(
                LocalHostFailureKind.ContextTooSmall, route.HostId, route.HostDisplayName,
                $"{route.HostDisplayName} model '{route.ModelKey}' context length is too small: {detail}");
        }

        var prefix = status is { } code ? $"HTTP {(int)code}: " : "";
        return new LocalHostUnavailableException(
            LocalHostFailureKind.HostError, route.HostId, route.HostDisplayName,
            $"{route.HostDisplayName} error: {prefix}{detail}");
    }

    private static void ParseChatEnd(
        JsonElement root,
        ref string? reportedModel,
        ref int outputTokens,
        StringBuilder text,
        ref double? firstVisibleMs,
        Stopwatch clock)
    {
        var result = root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object ? res : root;
        if (result.TryGetProperty("model_instance_id", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            reportedModel = modelEl.GetString();

        if (result.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object
            && stats.TryGetProperty("total_output_tokens", out var tokEl) && tokEl.ValueKind == JsonValueKind.Number)
        {
            outputTokens = tokEl.GetInt32();
        }

        // If the server sent the message only inside chat.end output[] without prior message.delta chunks, capture it.
        if (text.Length == 0 && result.TryGetProperty("output", out var outputArr) && outputArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputArr.EnumerateArray())
            {
                if (item.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "message", StringComparison.Ordinal)
                    && item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                    && content.GetString() is { Length: > 0 } contentText)
                {
                    text.Append(contentText);
                    if (!string.IsNullOrWhiteSpace(contentText))
                        firstVisibleMs ??= clock.Elapsed.TotalMilliseconds;
                }
            }
        }
    }

    private HttpRequestMessage CreatePost(Uri uri, object payload)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        // Keep-alive is required for Node.js/Express servers (Bionic, LM Studio): sending Connection: close on
        // HTTP/1.1 chunked SSE streams causes SocketsHttpHandler to race with Express's keep-alive parser and
        // receive empty HTTP 400 responses on subsequent requests.
        if (route.BearerToken is not null)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", route.BearerToken);
        return message;
    }

    private static string ExtractErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var err))
        {
            if (err.ValueKind == JsonValueKind.String)
                return err.GetString() ?? "unknown error";
            if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                return msg.GetString() ?? err.ToString();
            return err.ToString();
        }
        return root.TryGetProperty("message", out var direct) && direct.ValueKind == JsonValueKind.String
            ? direct.GetString() ?? "unknown error"
            : "unknown error";
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var trimmed = text.Trim();
            if (trimmed.StartsWith('{'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var extracted = ExtractErrorMessage(doc.RootElement);
                    if (!string.IsNullOrWhiteSpace(extracted) && extracted != "unknown error")
                        return extracted.Length > 300 ? extracted[..300] : extracted;
                }
                catch (JsonException)
                {
                }
            }

            return trimmed.Length > 300 ? trimmed[..300] : trimmed;
        }
        catch
        {
            return "";
        }
    }

    private static JsonDocument ParseEvent(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed LM Studio v1 stream event.", ex);
        }
    }
}
