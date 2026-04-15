using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

/// <summary>
/// Sends a single translation request to an OpenAI-compatible chat endpoint,
/// using the same prompt template as LlamaTranslationEngine.
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

    public async Task<(string Translation, long ElapsedMs)> TranslateAsync(
        string text, string srcLang, string tgtLang, CancellationToken ct = default)
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
            ["stream"] = false
        };

        if (!string.IsNullOrWhiteSpace(_config.ModelId))
            body["model"] = _config.ModelId;

        var url = _config.BaseUrl.TrimEnd('/') + "/v1/chat/completions";
        var sw = Stopwatch.StartNew();
        var response = await _http.PostAsync(
            url,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            ct).ConfigureAwait(false);
        sw.Stop();

        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var translation = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Empty response from model.");

        return (translation.Trim(), sw.ElapsedMilliseconds);
    }

    private static string GetLanguageName(string code) =>
        LanguageNames.TryGetValue(code, out var name) ? name : code;

    public void Dispose() => _http.Dispose();
}
