namespace LiveLingo.Core.Models;

/// <summary>
/// Contextual hints passed to <see cref="IModelSelector"/> to support
/// content-aware routing decisions.
///
/// Text-length escalation rules (applied on top of the user's
/// <see cref="TranslationRoutingMode"/> preference):
///
///   ≤  80 chars  → local fast (no escalation)
///    81–600 chars → default local; escalate if <see cref="IsHighQualityMode"/>
///   > 600 chars  → prefer cloud quality (escalated automatically)
///
/// <see cref="IsRareLanguagePair"/> also triggers cloud escalation regardless of length.
///
/// Escalation is skipped when the user has explicitly chosen
/// <see cref="TranslationRoutingMode.LocalOnly"/>.
/// </summary>
/// <param name="TextLength">Character count of the source text.</param>
/// <param name="IsHighQualityMode">
///   True when the user explicitly requests high-quality output
///   (e.g. "final-pass" before sending).
/// </param>
/// <param name="IsRareLanguagePair">
///   True when the source–target language pair is outside the common
///   zh / en / ja / ko set and likely benefits from a stronger cloud model.
/// </param>
public sealed record TranslationRoutingContext(
    int TextLength,
    bool IsHighQualityMode = false,
    bool IsRareLanguagePair = false)
{
    /// <summary>
    /// Build a context from the raw source text and language pair, applying
    /// the standard definition of "rare" language pairs used by the router.
    /// </summary>
    public static TranslationRoutingContext FromText(
        string text,
        string sourceLanguage,
        string targetLanguage,
        bool isHighQualityMode = false)
    {
        var isRare = !IsCommonLanguage(sourceLanguage) || !IsCommonLanguage(targetLanguage);
        return new TranslationRoutingContext(text.Length, isHighQualityMode, isRare);
    }

    private static bool IsCommonLanguage(string lang) =>
        lang is "zh" or "en" or "ja" or "ko";
}
