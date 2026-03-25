using System.Net.Http.Json;
using System.Text.Json;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

public sealed class LlamaTranslationEngine : ITranslationEngine
{
    private readonly QwenModelHost _host;
    private readonly HttpClient _http;
    private readonly ILogger<LlamaTranslationEngine> _logger;

    private static readonly Dictionary<string, (string EnglishName, string DisplayName)> Languages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = ("Chinese", "中文"),
            ["en"] = ("English", "English"),
            ["ja"] = ("Japanese", "日本語"),
            ["ko"] = ("Korean", "한국어"),
            ["fr"] = ("French", "Français"),
            ["de"] = ("German", "Deutsch"),
            ["es"] = ("Spanish", "Español"),
            ["ru"] = ("Russian", "Русский"),
            ["ar"] = ("Arabic", "العربية"),
            ["pt"] = ("Portuguese", "Português"),
        };

    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        Languages.Select(kv => new LanguageInfo(kv.Key, kv.Value.DisplayName)).ToList();

    public LlamaTranslationEngine(QwenModelHost host, HttpClient http, ILogger<LlamaTranslationEngine> logger)
    {
        _host = host;
        _http = http;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);

        var endpoint = await _host.GetOrStartServerAsync(ct);
        var url = $"{endpoint}/v1/chat/completions";

        var requestBody = LlamaServerChatRequest.CreateTranslation(text, srcName, tgtName);

        _logger.LogDebug("Translation prompt for {Src}→{Tgt}: {Prompt}", sourceLanguage, targetLanguage, requestBody.Messages[1].Content);

        var response = await _http.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var result = LlamaServerChatResponse.GetAssistantText(doc.RootElement);
        result = LlamaServerChatResponse.StripQwenThinkTags(result);

        if (string.IsNullOrWhiteSpace(result))
        {
            _logger.LogWarning(
                "Translation returned empty output for {Src}→{Tgt}. {Diag}",
                sourceLanguage,
                targetLanguage,
                LlamaServerChatResponse.DescribeFirstChoiceForLog(doc.RootElement));
            throw new InvalidOperationException("Translation returned empty output.");
        }

        _logger.LogDebug("Translated {Src}→{Tgt}: {In} → {Out}", sourceLanguage, targetLanguage, text, result);
        return result;
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        Languages.ContainsKey(sourceLanguage) && Languages.ContainsKey(targetLanguage);

    public void Dispose() { }

    private static string GetLanguageName(string code) =>
        Languages.TryGetValue(code, out var info) ? info.EnglishName : code;
}
