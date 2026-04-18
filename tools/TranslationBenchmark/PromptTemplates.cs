namespace TranslationBenchmark;

/// <summary>
/// Translation prompt variants for comparative evaluation. Every variant produces
/// a <c>(system, user)</c> pair consumed by <see cref="TranslationClient"/>; the
/// variant name is carried through to the report so prompt deltas stay attributable.
///
/// Gemma 4 notes: the instruction-tuned Gemma models (26B-A4B and E4B) honour plain
/// system-role messages but are sensitive to conversational preamble — the variants
/// prefixed <c>gemma4-</c> below are calibrated for that behaviour.
/// </summary>
public static class PromptTemplates
{
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
    /// Generic "Gemma-friendly" variant inherited from earlier runs. Kept for A/B baselines.
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
    /// Gemma 4 variant using explicit source/target tags that match Gemma 4's
    /// instruction-tuned examples. Keeps the system message short and free of
    /// negative-form rules (which Gemma sometimes echoes back).
    /// </summary>
    public static (string System, string User) Gemma4Tagged(string srcName, string tgtName, string text) => (
        System:
            $"You translate {srcName} to {tgtName}. Reply with only the {tgtName} translation.",
        User:
            $"<src>{text}</src>\n<dst lang=\"{tgtName}\">"
    );

    /// <summary>
    /// Gemma 4 variant tuned for LiveLingo's real-time overlay: emphasises that
    /// output should read as natural spoken {tgtName}, not literal word-by-word,
    /// which empirically reduces hallucinated punctuation on short utterances.
    /// </summary>
    public static (string System, string User) Gemma4Concise(string srcName, string tgtName, string text) => (
        System:
            $"Translate {srcName} into natural spoken {tgtName}. Output only the translation, preserve tone, no explanations.",
        User: text
    );

    /// <summary>
    /// Gemma 4 variant that wraps the output in <c>&lt;t&gt;…&lt;/t&gt;</c> so the
    /// post-processing pipeline can strip accidental preamble deterministically —
    /// trades a little verbosity for robust parsing under adversarial inputs.
    /// </summary>
    public static (string System, string User) Gemma4Structured(string srcName, string tgtName, string text) => (
        System:
            $"You are a {srcName}-to-{tgtName} translation engine. " +
            $"Return the translation wrapped in <t></t> tags. Produce no text outside those tags.",
        User:
            $"<source>{text}</source>\n<t>"
    );

    /// <summary>
    /// Minimal single-turn variant. Used as the floor baseline.
    /// </summary>
    public static (string System, string User) Minimal(string srcName, string tgtName, string text) => (
        System: $"You are a {srcName} to {tgtName} translator. Output only the translation.",
        User: text
    );

    public static (string System, string User) Build(string variantName, string srcName, string tgtName, string text) =>
        variantName switch
        {
            "gemma" => GemmaOptimized(srcName, tgtName, text),
            "gemma4-tagged" => Gemma4Tagged(srcName, tgtName, text),
            "gemma4-concise" => Gemma4Concise(srcName, tgtName, text),
            "gemma4-structured" => Gemma4Structured(srcName, tgtName, text),
            "minimal" => Minimal(srcName, tgtName, text),
            _ => Default(srcName, tgtName, text)
        };

    public static IReadOnlyList<string> AllVariantNames { get; } =
    [
        "default", "gemma", "gemma4-tagged", "gemma4-concise", "gemma4-structured", "minimal"
    ];
}
