using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost.Providers;

/// <summary>
/// Chat completion client speaking Ollama's native <c>POST /api/chat</c> NDJSON stream, with <c>"think": false</c>
/// to suppress reasoning latency and <c>"keep_alive": "10m"</c> aligned with LiveLingo's idle residency policy.
/// </summary>
public sealed class OllamaChatClient(HttpClient http, LocalHostRoute route) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var messages = request.System is null
            ? new[] { new { role = "user", content = request.Prompt } }
            : [new { role = "system", content = request.System }, new { role = "user", content = request.Prompt }];

        var options = new Dictionary<string, object>
        {
            ["temperature"] = request.Temperature,
            ["num_predict"] = request.MaxTokens
        };
        if (request.Extra.TryGetValue("top_p", out var topP))
            options["top_p"] = topP;
        if (request.Extra.TryGetValue("top_k", out var topK))
            options["top_k"] = topK;
        if (request.Extra.TryGetValue("repeat_penalty", out var rep))
            options["repeat_penalty"] = rep;
        if (request.Extra.TryGetValue("seed", out var seed))
            options["seed"] = seed;

        var payload = new Dictionary<string, object>
        {
            ["model"] = route.ModelKey,
            ["messages"] = messages,
            ["stream"] = true,
            ["think"] = false,
            ["keep_alive"] = "10m",
            ["options"] = options
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, route.ResolveEndpoint("/api/chat"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.ConnectionClose = true;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalHostUnavailableException(
                LocalHostFailureKind.ServerNotRunning, route.HostId, route.HostDisplayName,
                $"Cannot reach {route.HostDisplayName} at {route.BaseUri}: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                throw status == HttpStatusCode.NotFound
                    ? new LocalHostUnavailableException(
                        LocalHostFailureKind.ModelNotFound, route.HostId, route.HostDisplayName,
                        $"Model '{route.ModelKey}' was not found in {route.HostDisplayName}.")
                    : new LocalHostUnavailableException(
                        LocalHostFailureKind.HostError, route.HostId, route.HostDisplayName,
                        $"{route.HostDisplayName} returned HTTP {(int)status}.");
            }

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
            var text = new StringBuilder();
            double? firstVisibleMs = null;
            var outputTokens = 0;
            string? reportedModel = null;
            var done = false;

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var doc = ParseLine(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var err))
                {
                    var errText = err.GetString() ?? err.ToString();
                    var kind = errText.Contains("not found", StringComparison.OrdinalIgnoreCase)
                        ? LocalHostFailureKind.ModelNotFound
                        : LocalHostFailureKind.HostError;
                    throw new LocalHostUnavailableException(kind, route.HostId, route.HostDisplayName, errText);
                }

                if (root.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                    reportedModel = mEl.GetString();

                if (root.TryGetProperty("message", out var msgObj) && msgObj.ValueKind == JsonValueKind.Object
                    && msgObj.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String
                    && contentEl.GetString() is { Length: > 0 } chunk)
                {
                    text.Append(chunk);
                    if (!string.IsNullOrWhiteSpace(chunk))
                        firstVisibleMs ??= clock.Elapsed.TotalMilliseconds;
                    request.OnDelta?.Invoke(chunk);
                }

                if (root.TryGetProperty("eval_count", out var evalCount) && evalCount.ValueKind == JsonValueKind.Number)
                    outputTokens = evalCount.GetInt32();

                if (root.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True)
                {
                    done = true;
                    break;
                }
            }

            if (!done || firstVisibleMs is null)
                throw new InvalidDataException($"Incomplete Ollama chat stream: done={done}.");

            return new ChatCompletion(
                text.ToString().Trim(),
                firstVisibleMs.Value,
                clock.Elapsed.TotalMilliseconds,
                outputTokens,
                reportedModel ?? route.ModelKey);
        }
    }

    private static JsonDocument ParseLine(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed Ollama stream event.", ex);
        }
    }
}
