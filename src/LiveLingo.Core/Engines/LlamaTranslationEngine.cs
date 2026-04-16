using LiveLingo.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

/// <summary>
/// Translation engine backed by the MEA <see cref="IChatClient"/> pipeline.
/// The pipeline is assembled in DI (from outer to inner):
///   LoggingChatClient → DistributedCachingChatClient → TranslationChatClient
///
/// Same-text / same-language-pair requests return from the in-memory cache
/// without hitting the model again.
/// </summary>
public sealed class LlamaTranslationEngine : ITranslationEngine
{
    private readonly IChatClient _chatClient;
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

    public LlamaTranslationEngine(IChatClient chatClient, ILogger<LlamaTranslationEngine> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                $"You are an expert translation engine. Your task is to translate the source text from {srcName} to {tgtName}.\n\n" +
                "Rules:\n" +
                $"1. Output ONLY the final {tgtName} translation.\n" +
                $"2. Do NOT output any {srcName} text.\n" +
                "3. Do NOT output any explanations, conversational text, or notes.\n" +
                "4. Do not use <think> tags or output any thought process."),
            new(ChatRole.User,
                $"Translate the following {srcName} text to {tgtName}:\n\n<source>\n{text}\n</source>")
        };

        var defaults = ModelInvocationOptions.CreateTranslationDefaults();
        var options = new ChatOptions
        {
            Temperature = defaults.Temperature,
            MaxOutputTokens = defaults.MaxTokens,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["sourceLang"] = sourceLanguage,
                ["targetLang"] = targetLanguage,
                ["taskType"] = nameof(ModelTaskType.Translation),
                ["textLength"] = text.Length
            }
        };

        _logger.LogDebug("Translation prompt for {Src}→{Tgt}: {Prompt}", sourceLanguage, targetLanguage, messages[1].Text);

        var response = await _chatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
        var result = response.Text?.Trim();

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Translation returned empty output.");

        _logger.LogDebug("Translated {Src}→{Tgt}: {In} → {Out}", sourceLanguage, targetLanguage, text, result);
        return result;
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        Languages.ContainsKey(sourceLanguage) && Languages.ContainsKey(targetLanguage);

    public void Dispose() => _chatClient.Dispose();

    private static string GetLanguageName(string code) =>
        Languages.TryGetValue(code, out var info) ? info.EnglishName : code;
}
