namespace LiveLingo.Core.Engines;

public sealed class StubTranslationEngine : ITranslationEngine
{
    public Task<string> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult($"[{targetLanguage.ToUpperInvariant()}] {text}");
    }

    public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) => true;

    public void Dispose() { }
}
