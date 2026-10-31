using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LiveLingo.Core.Inference;

/// <param name="endpoint">The full chat-completions URL (for example <c>http://127.0.0.1:8080/v1/chat/completions</c>).</param>
/// <param name="model">Model or deployment name; local llama-server serves one model and needs none.</param>
/// <param name="bearerToken">API key sent as a bearer token; null for the local server.</param>
/// <param name="sendProviderExtras">
/// Whether <see cref="ChatRequest.Extra"/> is sent. Those fields are llama-server sampling options that hosted
/// OpenAI-compatible services reject.
/// </param>
public sealed class OpenAiChatClient(
    HttpClient http,
    Uri endpoint,
    string? model = null,
    string? bearerToken = null,
    bool sendProviderExtras = true) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var messages = request.System is null
            ? new[] { new { role = "user", content = request.Prompt } }
            : [new { role = "system", content = request.System }, new { role = "user", content = request.Prompt }];
        var payload = new Dictionary<string, object>(sendProviderExtras ? request.Extra : new Dictionary<string, object>())
        {
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };
        if (model is not null)
            payload["model"] = model;
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        // llama-server drops idle keep-alive sockets; reusing one fails the next request with a connection error.
        message.Headers.ConnectionClose = true;
        if (bearerToken is not null)
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var text = new StringBuilder();
        double? firstVisibleMs = null;
        var outputTokens = 0;
        string? finish = null;
        var done = false;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                done = true;
                break;
            }

            using var json = ParseEvent(data);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidDataException("Inference server error: " + error);
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("completion_tokens", out var tokens))
                outputTokens = tokens.GetInt32();
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                finish = reason.GetString();
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } chunk)
            {
                text.Append(chunk);
                if (!string.IsNullOrWhiteSpace(chunk))
                    firstVisibleMs ??= clock.Elapsed.TotalMilliseconds;
            }
        }

        if (!done || finish != "stop" || firstVisibleMs is null)
            throw new InvalidDataException($"Incomplete completion stream: done={done}, finish={finish ?? "missing"}.");

        return new ChatCompletion(text.ToString().Trim(), firstVisibleMs.Value, clock.Elapsed.TotalMilliseconds, outputTokens);
    }

    private static JsonDocument ParseEvent(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed completion stream event.", ex);
        }
    }
}
