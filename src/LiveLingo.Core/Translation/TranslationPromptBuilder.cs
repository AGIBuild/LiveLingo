using LiveLingo.Core.Models;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Builds translation prompt messages tailored to each local model's
/// <see cref="LocalModelChatTemplate"/>.
///
/// Design note: messages produced by this builder are used as the ACTUAL
/// invocation payload; the MEA cache layer uses the caller-supplied canonical
/// (Default-template) messages as its cache key, so cache hits are served
/// correctly regardless of which template variant was used for generation.
/// </summary>
public static class TranslationPromptBuilder
{
    public static IReadOnlyList<ModelChatMessage> Build(
        string sourceText,
        string sourceLanguageName,
        string targetLanguageName,
        LocalModelChatTemplate template) => template switch
    {
        LocalModelChatTemplate.Gemma => BuildGemmaOptimized(sourceText, sourceLanguageName, targetLanguageName),
        LocalModelChatTemplate.Qwen => BuildQwenOptimized(sourceText, sourceLanguageName, targetLanguageName),
        _ => BuildDefault(sourceText, sourceLanguageName, targetLanguageName)
    };

    /// <summary>
    /// Standard prompt used for generic instruct models and cloud providers.
    /// Also used as the canonical cache key in <see cref="LlamaTranslationEngine"/>.
    /// </summary>
    public static IReadOnlyList<ModelChatMessage> BuildDefault(
        string sourceText, string srcName, string tgtName) =>
    [
        new("system",
            $"You are an expert translation engine. Your task is to translate the source text from {srcName} to {tgtName}.\n\n" +
            "Rules:\n" +
            $"1. Output ONLY the final {tgtName} translation.\n" +
            $"2. Do NOT output any {srcName} text.\n" +
            "3. Do NOT output any explanations, conversational text, or notes.\n" +
            "4. Do not use <think> tags or output any thought process."),
        new("user",
            $"Translate the following {srcName} text to {tgtName}:\n\n<source>\n{sourceText}\n</source>")
    ];

    /// <summary>
    /// Gemma-optimized prompt: explicit "begin immediately" instruction removes
    /// preamble artifacts that Gemma models sometimes emit with the default template.
    /// </summary>
    private static IReadOnlyList<ModelChatMessage> BuildGemmaOptimized(
        string sourceText, string srcName, string tgtName) =>
    [
        new("system",
            $"You are a professional translator from {srcName} to {tgtName}.\n" +
            $"Respond with the {tgtName} translation only. " +
            "Do not include any explanation, commentary, or the original text. " +
            "Begin your response immediately with the first word of the translation."),
        new("user",
            $"<source lang=\"{srcName}\">\n{sourceText}\n</source>\n\nTranslate to {tgtName}:")
    ];

    /// <summary>
    /// Qwen-optimized prompt: adds explicit no-thinking instruction since Qwen
    /// models may produce &lt;think&gt; blocks when using instruct format.
    /// </summary>
    private static IReadOnlyList<ModelChatMessage> BuildQwenOptimized(
        string sourceText, string srcName, string tgtName) =>
    [
        new("system",
            $"You are an expert translation engine.\n" +
            $"Translate from {srcName} to {tgtName}.\n" +
            "Output ONLY the translated text. " +
            "Do not think out loud. Do not use <think> tags. " +
            "Do not explain. Do not add notes."),
        new("user",
            $"Translate to {tgtName}:\n{sourceText}")
    ];
}
