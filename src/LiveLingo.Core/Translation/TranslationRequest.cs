namespace LiveLingo.Core.Translation;

/// <summary>Only Chinese and English are validated (poc/text-translation/REPORT.md).</summary>
public enum TargetLanguage
{
    English,
    Chinese
}

public sealed record GlossaryTerm(string Source, string Target);

/// <summary>
/// A recurring speech or accent misrecognition (<paramref name="Spoken"/>) and the canonical wording
/// (<paramref name="Canonical"/>) the user actually said, for example <c>Scale =&gt; Skill</c> or <c>六零九 =&gt; LiveLingo</c>.
/// </summary>
public sealed record GlossaryCorrection(string Spoken, string Canonical);

/// <param name="Source">Text to translate as the user wrote it; the translator protects literals itself.</param>
/// <param name="Context">Read-only background that must not be translated; empty when unused.</param>
public sealed record TranslationRequest(
    string Source,
    TargetLanguage Target,
    IReadOnlyList<GlossaryTerm> Terms,
    string Context = "");
