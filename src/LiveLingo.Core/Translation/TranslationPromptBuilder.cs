using System.Text;
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
        LocalModelChatTemplate template,
        IReadOnlyList<GlossaryEntry>? glossaryHints = null) => template switch
    {
        LocalModelChatTemplate.Gemma => BuildGemmaOptimized(sourceText, sourceLanguageName, targetLanguageName, glossaryHints),
        LocalModelChatTemplate.Qwen => BuildQwenOptimized(sourceText, sourceLanguageName, targetLanguageName, glossaryHints),
        _ => BuildDefault(sourceText, sourceLanguageName, targetLanguageName, glossaryHints)
    };

    /// <summary>
    /// Standard prompt used for generic instruct models and cloud providers.
    /// Also used as the canonical cache key in <see cref="LlamaTranslationEngine"/>.
    /// </summary>
    public static IReadOnlyList<ModelChatMessage> BuildDefault(
        string sourceText, string srcName, string tgtName,
        IReadOnlyList<GlossaryEntry>? glossaryHints = null) =>
    [
        new("system",
            $"You are an expert translation engine. Your task is to translate the source text from {srcName} to {tgtName}.\n\n" +
            "Rules:\n" +
            $"1. Output ONLY the final {tgtName} translation.\n" +
            $"2. Do NOT output any {srcName} text.\n" +
            "3. Do NOT output any explanations, conversational text, or notes.\n" +
            "4. Do not use <think> tags or output any thought process." +
            FormatGlossarySection(glossaryHints)),
        new("user",
            $"Translate the following {srcName} text to {tgtName}:\n\n<source>\n{sourceText}\n</source>")
    ];

    /// <summary>
    /// Gemma-optimized prompt: explicit "begin immediately" instruction removes
    /// preamble artifacts that Gemma models sometimes emit with the default template.
    /// </summary>
    private static IReadOnlyList<ModelChatMessage> BuildGemmaOptimized(
        string sourceText, string srcName, string tgtName,
        IReadOnlyList<GlossaryEntry>? glossaryHints) =>
    [
        new("system",
            $"You are a professional translator from {srcName} to {tgtName}.\n" +
            $"Respond with the {tgtName} translation only. " +
            "Do not include any explanation, commentary, or the original text. " +
            "Begin your response immediately with the first word of the translation." +
            FormatGlossarySection(glossaryHints)),
        new("user",
            $"<source lang=\"{srcName}\">\n{sourceText}\n</source>\n\nTranslate to {tgtName}:")
    ];

    /// <summary>
    /// Qwen-optimized prompt: adds explicit no-thinking instruction since Qwen
    /// models may produce &lt;think&gt; blocks when using instruct format.
    /// </summary>
    private static IReadOnlyList<ModelChatMessage> BuildQwenOptimized(
        string sourceText, string srcName, string tgtName,
        IReadOnlyList<GlossaryEntry>? glossaryHints) =>
    [
        new("system",
            $"You are an expert translation engine.\n" +
            $"Translate from {srcName} to {tgtName}.\n" +
            "Output ONLY the translated text. " +
            "Do not think out loud. Do not use <think> tags. " +
            "Do not explain. Do not add notes." +
            FormatGlossarySection(glossaryHints)),
        new("user",
            $"Translate to {tgtName}:\n{sourceText}")
    ];

    private static string FormatGlossarySection(IReadOnlyList<GlossaryEntry>? hints)
    {
        if (hints is not { Count: > 0 }) return string.Empty;

        var sb = new StringBuilder("\n\n[Glossary - translate these terms exactly as specified]:");
        foreach (var entry in hints)
            sb.Append("\n- ").Append(entry.SourceTerm).Append(" → ").Append(entry.TargetTerm);
        return sb.ToString();
    }
}
