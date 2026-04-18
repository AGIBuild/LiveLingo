namespace LiveLingo.Core.Translation;

/// <summary>
/// Thread-safe glossary backed by <see cref="CoreOptions.Glossary"/>.
/// Reads the live options reference on every lookup so runtime setting changes
/// are reflected immediately without restart.
/// </summary>
public sealed class InMemoryTranslationGlossary : ITranslationGlossary
{
    private const int MaxHintsPerRequest = 12;

    private readonly CoreOptions _options;

    public InMemoryTranslationGlossary(CoreOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<GlossaryEntry> GetRelevantEntries(
        string sourceText, string sourceLanguage, string targetLanguage)
    {
        var entries = _options.Glossary;
        if (entries.Count == 0) return [];

        var results = new List<GlossaryEntry>(capacity: Math.Min(entries.Count, MaxHintsPerRequest));

        foreach (var entry in entries)
        {
            if (results.Count >= MaxHintsPerRequest) break;

            if (!LanguageMatches(entry.SourceLanguage, sourceLanguage)) continue;
            if (!LanguageMatches(entry.TargetLanguage, targetLanguage)) continue;

            if (sourceText.Contains(entry.SourceTerm, StringComparison.OrdinalIgnoreCase))
                results.Add(entry);
        }

        return results;
    }

    private static bool LanguageMatches(string? constraint, string actual) =>
        constraint is null || string.Equals(constraint, actual, StringComparison.OrdinalIgnoreCase);
}
