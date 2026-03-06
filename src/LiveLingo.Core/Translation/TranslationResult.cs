namespace LiveLingo.Core.Translation;

public record TranslationResult(
    string Text,
    string DetectedSourceLanguage,
    string RawTranslation,
    TimeSpan TranslationDuration,
    TimeSpan? PostProcessingDuration);
