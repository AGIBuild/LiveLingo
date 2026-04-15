using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

public sealed class LlamaTranslationEngine : ITranslationEngine
{
    private readonly IModelSelector _selector;
    private readonly IModelInvocationService _invocationService;
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

    public LlamaTranslationEngine(
        IModelSelector selector,
        IModelInvocationService invocationService,
        ILogger<LlamaTranslationEngine> logger)
    {
        _selector = selector;
        _invocationService = invocationService;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);
        var profile = _selector.SelectTranslationProfile(sourceLanguage, targetLanguage);
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [
                new ModelChatMessage(
                    "system",
                    $"You are an expert translation engine. Your task is to translate the source text from {srcName} to {tgtName}.\n\n" +
                    "Rules:\n" +
                    $"1. Output ONLY the final {tgtName} translation.\n" +
                    $"2. Do NOT output any {srcName} text.\n" +
                    "3. Do NOT output any explanations, conversational text, or notes.\n" +
                    "4. Do not use <think> tags or output any thought process."),
                new ModelChatMessage(
                    "user",
                    $"Translate the following {srcName} text to {tgtName}:\n\n<source>\n{text}\n</source>")
            ],
            ModelInvocationOptions.CreateTranslationDefaults());

        _logger.LogDebug(
            "Translation prompt for {Src}→{Tgt}: {Prompt}",
            sourceLanguage,
            targetLanguage,
            request.Messages[1].Content);

        var result = (await _invocationService.InvokeAsync(request, ct).ConfigureAwait(false)).Text;

        if (string.IsNullOrWhiteSpace(result))
        {
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
