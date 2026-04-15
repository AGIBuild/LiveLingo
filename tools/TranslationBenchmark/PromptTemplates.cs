namespace TranslationBenchmark;

/// <summary>
/// Translation prompt variants for comparative evaluation.
/// Each variant name is used in benchmark config and report output.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// Current production prompt used by LlamaTranslationEngine.
    /// Shared by all models (Gemma 4, Qwen, Cloud).
    /// </summary>
    public static (string System, string User) Default(string srcName, string tgtName, string text) => (
        System:
            $"You are an expert translation engine. Your task is to translate the source text from {srcName} to {tgtName}.\n\n" +
            "Rules:\n" +
            $"1. Output ONLY the final {tgtName} translation.\n" +
            $"2. Do NOT output any {srcName} text.\n" +
            "3. Do NOT output any explanations, conversational text, or notes.\n" +
            "4. Do not use <think> tags or output any thought process.",
        User:
            $"Translate the following {srcName} text to {tgtName}:\n\n<source>\n{text}\n</source>"
    );

    /// <summary>
    /// Gemma 4-optimized variant: drops the Qwen-specific think-tag rule,
    /// uses an explicit "begin immediately" instruction, and strengthens the
    /// output-only constraint. Best suited for Gemma instruction-tuned models.
    /// </summary>
    public static (string System, string User) GemmaOptimized(string srcName, string tgtName, string text) => (
        System:
            $"You are a professional translator from {srcName} to {tgtName}.\n" +
            $"Respond with the {tgtName} translation only. " +
            "Do not include any explanation, commentary, or the original text. " +
            "Begin your response immediately with the first word of the translation.",
        User:
            $"<source lang=\"{srcName}\">\n{text}\n</source>\n\n" +
            $"Translate to {tgtName}:"
    );

    /// <summary>
    /// Minimal variant: single-turn, no system message.
    /// Tests whether complex system prompts actually help.
    /// </summary>
    public static (string System, string User) Minimal(string srcName, string tgtName, string text) => (
        System: $"You are a {srcName} to {tgtName} translator. Output only the translation.",
        User: text
    );

    public static (string System, string User) Build(string variantName, string srcName, string tgtName, string text) =>
        variantName switch
        {
            "gemma" => GemmaOptimized(srcName, tgtName, text),
            "minimal" => Minimal(srcName, tgtName, text),
            _ => Default(srcName, tgtName, text)
        };
}
