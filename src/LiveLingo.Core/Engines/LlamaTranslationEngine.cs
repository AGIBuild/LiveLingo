using System.Runtime.CompilerServices;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Engines;

/// <summary>
/// Translation engine backed by the MEA <see cref="IChatClient"/> pipeline.
/// The pipeline is assembled in DI (from outer to inner):
///   LoggingChatClient → DistributedCachingChatClient → TranslationChatClient
///
/// Message layout:
/// • The messages passed to <see cref="IChatClient"/> use the Default template
///   and serve as the stable cache key for <see cref="DistributedCachingChatClient"/>.
/// • <see cref="TranslationChatClient"/> rebuilds model-specific messages
///   from <c>AdditionalProperties["sourceText"]</c> using
///   <see cref="TranslationPromptBuilder"/> before calling the actual model,
///   so the optimal prompt is always used for inference.
/// </summary>
public sealed class LlamaTranslationEngine : IChatPathTranslationEngine
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

        // Canonical Default-template messages → stable cache key for DistributedCachingChatClient.
        // TranslationChatClient will replace these with the model-optimal variant before invocation.
        var defaultMessages = TranslationPromptBuilder.BuildDefault(text, srcName, tgtName);
        var chatMessages = defaultMessages
            .Select(m => new ChatMessage(new ChatRole(m.Role), m.Content))
            .ToList();

        var defaults = ModelInvocationOptions.CreateTranslationDefaults();
        var options = new ChatOptions
        {
            Temperature = defaults.Temperature,
            MaxOutputTokens = defaults.MaxTokens,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                // Routing context
                ["sourceLang"] = sourceLanguage,
                ["targetLang"] = targetLanguage,
                ["taskType"] = nameof(ModelTaskType.Translation),
                ["textLength"] = text.Length,
                // Prompt-template context (TranslationChatClient rebuilds messages from these)
                ["sourceText"] = text,
                ["sourceLangName"] = srcName,
                ["targetLangName"] = tgtName
            }
        };

        _logger.LogDebug("Translation prompt for {Src}→{Tgt}: {Prompt}", sourceLanguage, targetLanguage, chatMessages[1].Text);

        var response = await _chatClient.GetResponseAsync(chatMessages, options, ct).ConfigureAwait(false);
        var result = response.Text?.Trim();

        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("Translation returned empty output.");

        _logger.LogDebug("Translated {Src}→{Tgt}: {In} → {Out}", sourceLanguage, targetLanguage, text, result);
        return result;
    }

    public async IAsyncEnumerable<TranslationDelta> TranslateStreamingAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var srcName = GetLanguageName(sourceLanguage);
        var tgtName = GetLanguageName(targetLanguage);

        var defaultMessages = TranslationPromptBuilder.BuildDefault(text, srcName, tgtName);
        var chatMessages = defaultMessages
            .Select(m => new ChatMessage(new ChatRole(m.Role), m.Content))
            .ToList();

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
                ["textLength"] = text.Length,
                ["sourceText"] = text,
                ["sourceLangName"] = srcName,
                ["targetLangName"] = tgtName
            }
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(chatMessages, options, ct)
                           .ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;

            // replaceAll = true → quality guard triggered cloud escalation; signal replacement.
            var isReplacement = update.AdditionalProperties?.TryGetValue("replaceAll", out var r) == true
                                && r is true;
            yield return new TranslationDelta(update.Text, isReplacement);
        }
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) =>
        Languages.ContainsKey(sourceLanguage) && Languages.ContainsKey(targetLanguage);

    public void Dispose() => _chatClient.Dispose();

    private static string GetLanguageName(string code) =>
        Languages.TryGetValue(code, out var info) ? info.EnglishName : code;
}
