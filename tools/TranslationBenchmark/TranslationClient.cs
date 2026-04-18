using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

/// <summary>
/// Sends a single translation request to an OpenAI-compatible chat endpoint,
/// using the configured prompt template. Supports both blocking and streaming
/// requests so the benchmark can measure first-token latency — the metric that
/// drives the production route plan's <c>FirstTokenBudget</c>.
/// </summary>
public sealed class TranslationClient : IDisposable
{
    private static readonly Dictionary<string, string> LanguageNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = "Chinese",
            ["en"] = "English",
            ["ja"] = "Japanese",
            ["ko"] = "Korean",
            ["fr"] = "French",
            ["de"] = "German",
        };

    private readonly HttpClient _http;
    private readonly ModelEndpointConfig _config;

    public TranslationClient(ModelEndpointConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds) };
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    public Task<TranslationInvocation> TranslateAsync(
        string text, string srcLang, string tgtLang, CancellationToken ct = default) =>
        _config.Streaming
            ? TranslateStreamingAsync(text, srcLang, tgtLang, ct)
            : TranslateBlockingAsync(text, srcLang, tgtLang, ct);

    private async Task<TranslationInvocation> TranslateBlockingAsync(
        string text, string srcLang, string tgtLang, CancellationToken ct)
    {
        var (body, url) = BuildRequest(text, srcLang, tgtLang, streaming: false);

        var sw = Stopwatch.StartNew();
        var response = await _http.PostAsync(url,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            ct).ConfigureAwait(false);
        sw.Stop();

        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var translation = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Empty response from model.");

        return new TranslationInvocation(translation.Trim(), sw.ElapsedMilliseconds, FirstTokenMs: null);
    }

    private async Task<TranslationInvocation> TranslateStreamingAsync(
        string text, string srcLang, string tgtLang, CancellationToken ct)
    {
        var (body, url) = BuildRequest(text, srcLang, tgtLang, streaming: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var builder = new StringBuilder();
        long? firstTokenMs = null;
        await foreach (var delta in SseChatStreamParser.EnumerateDeltasAsync(stream, ct)
            .ConfigureAwait(false))
        {
            if (firstTokenMs is null)
                firstTokenMs = sw.ElapsedMilliseconds;
            builder.Append(delta);
        }
        sw.Stop();

        if (builder.Length == 0)
            throw new InvalidOperationException("Stream closed without emitting any content.");

        return new TranslationInvocation(builder.ToString().Trim(), sw.ElapsedMilliseconds, firstTokenMs);
    }

    private (JsonObject body, string url) BuildRequest(string text, string srcLang, string tgtLang, bool streaming)
    {
        var srcName = GetLanguageName(srcLang);
        var tgtName = GetLanguageName(tgtLang);
        var (systemPrompt, userPrompt) = PromptTemplates.Build(_config.PromptVariant, srcName, tgtName, text);

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user",   ["content"] = userPrompt }
        };

        var body = new JsonObject
        {
            ["messages"] = messages,
            ["temperature"] = 0.1,
            ["max_tokens"] = 512,
            ["stream"] = streaming
        };

        if (!string.IsNullOrWhiteSpace(_config.ModelId))
            body["model"] = _config.ModelId;

        var url = _config.BaseUrl.TrimEnd('/') + "/v1/chat/completions";
        return (body, url);
    }

    private static string GetLanguageName(string code) =>
        LanguageNames.TryGetValue(code, out var name) ? name : code;

    public void Dispose() => _http.Dispose();
}

/// <summary>Single translation call result as observed by the benchmark client.</summary>
public sealed record TranslationInvocation(string Translation, long ElapsedMs, long? FirstTokenMs);
