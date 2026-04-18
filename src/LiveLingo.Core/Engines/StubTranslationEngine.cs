namespace LiveLingo.Core.Engines;

public sealed class StubTranslationEngine : ITranslationEngine
{
    public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        [new("en", "English"), new("zh", "中文"), new("ja", "日本語")];

    public Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult($"[{targetLanguage.ToUpperInvariant()}] {text}");
    }

    public async IAsyncEnumerable<TranslationDelta> TranslateStreamingAsync(
        string text, string sourceLanguage, string targetLanguage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new TranslationDelta($"[{targetLanguage.ToUpperInvariant()}] {text}");
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) => true;

    public void Dispose() { }
}
