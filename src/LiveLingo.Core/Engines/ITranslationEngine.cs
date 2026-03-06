namespace LiveLingo.Core.Engines;

public interface ITranslationEngine : IDisposable
{
    Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken ct = default);

    bool SupportsLanguagePair(string sourceLanguage, string targetLanguage);
}
