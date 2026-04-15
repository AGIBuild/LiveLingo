using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

/// <summary>
/// Uses an OpenAI-compatible endpoint as a judge to score translation quality 1-10.
/// </summary>
public sealed class LlmJudge : IDisposable
{
    private readonly HttpClient _http;
    private readonly ModelEndpointConfig _config;

    public LlmJudge(ModelEndpointConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    /// <summary>Returns a quality score 1–10, or null if judging fails.</summary>
    public async Task<double?> JudgeAsync(
        string source, string translation, string srcLang, string tgtLang,
        CancellationToken ct = default)
    {
        var prompt =
            $"You are an expert translation evaluator.\n" +
            $"Source language: {srcLang}\nTarget language: {tgtLang}\n\n" +
            $"Source: {source}\nTranslation: {translation}\n\n" +
            $"Rate the translation quality from 1 to 10 based on:\n" +
            $"- Accuracy (meaning preserved)\n- Fluency (natural in target language)\n- Completeness\n\n" +
            $"Respond with ONLY a single integer between 1 and 10. No explanation.";

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = prompt }
        };

        var body = new JsonObject
        {
            ["messages"] = messages,
            ["temperature"] = 0.0,
            ["max_tokens"] = 5
        };

        if (!string.IsNullOrWhiteSpace(_config.ModelId))
            body["model"] = _config.ModelId;

        try
        {
            var url = _config.BaseUrl.TrimEnd('/') + "/v1/chat/completions";
            var response = await _http.PostAsync(
                url,
                new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var text = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()?.Trim();

            return double.TryParse(text, out var score) && score >= 1 && score <= 10 ? score : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
