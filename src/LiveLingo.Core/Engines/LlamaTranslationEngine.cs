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

    internal static readonly string[] StopSequences = ["</s>", "<|im_end|>", "</think>"];

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
        var url = $"{endpoint}/completion";

        var systemPrompt = $"You are an expert translation engine. Translate the text from {srcName} to {tgtName}.\nRules:\n1. Output ONLY the translated text in {tgtName}.\n2. Do NOT repeat or output the original {srcName} text.\n3. Do NOT add any explanations, notes, or conversational text.\n4. Do NOT use <think> tags or output any thought process.";
        var prompt = $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\nTranslate this to {tgtName}:\n\n{text}<|im_end|>\n<|im_start|>assistant\n";

        _logger.LogDebug("Translation prompt for {Src}→{Tgt}: {Prompt}", sourceLanguage, targetLanguage, prompt);

        var requestBody = new
        {
            prompt = prompt,
            n_predict = 512,
            temperature = 0.1f,
            top_p = 0.95f,
            stop = StopSequences,
            stream = false
        };

        var response = await _http.PostAsJsonAsync(url, requestBody, ct);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("content").GetString()?.Trim() ?? string.Empty;

        // Clean up <think> tags if the model still generated them
        if (result.Contains("</think>"))
        {
            var parts = result.Split("</think>");
            result = parts.Last().Trim();
        }
        else if (result.StartsWith("<think>"))
        {
            // Model generated <think> but didn't finish it
            result = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            _logger.LogWarning("Translation returned empty output for {Src}→{Tgt}", sourceLanguage, targetLanguage);
            return text;
        }

        _logger.LogDebug("Translated {Src}→{Tgt}: {In} → {Out}", sourceLanguage, targetLanguage, text, result);
        return result;
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        Languages.ContainsKey(sourceLanguage) && Languages.ContainsKey(targetLanguage);

    public void Dispose() { }

    internal static string BuildPrompt(string text, string sourceLanguage, string targetLanguage)
    {
        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);
        var systemPrompt = $"You are a professional translator. Translate the user's text from {srcName} to {tgtName}. Output ONLY the translated text, nothing else. Do not output any thought process or explanation. Do not use <think> tags.";
        return $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{text}<|im_end|>\n<|im_start|>assistant\n";
    }

    private static string GetLanguageName(string code) =>
        Languages.TryGetValue(code, out var info) ? info.EnglishName : code;
}