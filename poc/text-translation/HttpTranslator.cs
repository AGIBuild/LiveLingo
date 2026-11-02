using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TranslationPoc;

public sealed class HttpTranslator(HttpClient client, double temperature = 0, bool protectLiterals = true, int maxTokens = 2048) : ITranslator
{
    public async Task<Translation> TranslateAsync(Request request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var protectedText = protectLiterals ? ProtectedText.Encode(request.Source) : new ProtectedText(request.Source, new Dictionary<string, string>());
        var payload = new Dictionary<string, object>
        {
            ["messages"] = new[] { new { role = "user", content = Prompt.Build(request with { Source = protectedText.Source }) } },
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["temperature"] = temperature,
            ["top_p"] = 0.6,
            ["top_k"] = 20,
            ["repeat_penalty"] = 1.05,
            ["seed"] = 42,
            ["max_tokens"] = Math.Min(maxTokens, protectedText.Source.Length * 2 + 128),
            ["cache_prompt"] = false
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.ConnectionClose = true;
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        var text = new StringBuilder();
        double? first = null;
        var outputTokens = 0;
        string? finish = null;
        var done = false;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { done = true; break; }
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidDataException("Translation server: " + error);
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("completion_tokens", out var tokens))
                outputTokens = tokens.GetInt32();
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                finish = reason.GetString();
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } chunk)
            {
                text.Append(chunk);
                if (!string.IsNullOrWhiteSpace(chunk))
                    first ??= clock.Elapsed.TotalMilliseconds;
            }
        }
        if (!done || finish != "stop" || first is null || string.IsNullOrWhiteSpace(text.ToString()))
            throw new InvalidDataException($"Incomplete translation stream: done={done}, finish={finish ?? "missing"}.");
        var translated = text.ToString().Trim();
        return new Translation(protectedText.Restore(translated), first.Value, clock.Elapsed.TotalMilliseconds, outputTokens);
    }
}
