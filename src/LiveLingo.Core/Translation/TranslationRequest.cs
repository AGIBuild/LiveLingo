namespace LiveLingo.Core.Translation;

/// <summary>Only Chinese and English are validated (poc/text-translation/REPORT.md).</summary>
public enum TargetLanguage
{
    English,
    Chinese
}

public sealed record GlossaryTerm(string Source, string Target);

/// <param name="Source">Text to translate as the user wrote it; the translator protects literals itself.</param>
/// <param name="Context">Read-only background that must not be translated; empty when unused.</param>
public sealed record TranslationRequest(
    string Source,
    TargetLanguage Target,
    IReadOnlyList<GlossaryTerm> Terms,
    string Context = "");
