namespace LiveLingo.Core.Translation;

/// <summary>
/// A single term mapping in the translation glossary.
/// Language constraints are BCP-47 codes; null means the entry applies to all language pairs.
/// </summary>
public sealed record GlossaryEntry(
    string SourceTerm,
    string TargetTerm,
    string? SourceLanguage = null,
    string? TargetLanguage = null);

/// <summary>
/// Provides glossary lookups for <see cref="TranslationChatClient"/>.
/// </summary>
public interface ITranslationGlossary
{
    /// <summary>
    /// Returns glossary entries whose <see cref="GlossaryEntry.SourceTerm"/> appears in
    /// <paramref name="sourceText"/> and whose language constraints match the given pair.
    /// </summary>
    IReadOnlyList<GlossaryEntry> GetRelevantEntries(
        string sourceText, string sourceLanguage, string targetLanguage);
}
