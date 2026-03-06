using LiveLingo.Core.Processing;

namespace LiveLingo.Core.Translation;

public record TranslationRequest(
    string SourceText,
    string? SourceLanguage,
    string TargetLanguage,
    ProcessingOptions? PostProcessing);
