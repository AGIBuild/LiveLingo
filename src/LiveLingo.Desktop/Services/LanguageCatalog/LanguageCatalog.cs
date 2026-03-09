using LiveLingo.Core.Engines;

namespace LiveLingo.Desktop.Services.LanguageCatalog;

public sealed class LanguageCatalog : ILanguageCatalog
{
    public static IReadOnlyList<LanguageInfo> DefaultLanguages { get; } =
    [
        new("zh", "Chinese (中文)"),
        new("en", "English"),
        new("ja", "Japanese (日本語)"),
        new("ko", "Korean (한국어)"),
        new("fr", "French"),
        new("de", "German"),
        new("es", "Spanish"),
        new("ru", "Russian"),
        new("ar", "Arabic"),
        new("pt", "Portuguese"),
    ];

    public IReadOnlyList<LanguageInfo> All => DefaultLanguages;
}
