namespace LiveLingo.Core.LanguageDetection;

public sealed class StubLanguageDetector : ILanguageDetector
{
    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new DetectionResult("zh", 1.0f));
    }

    public void Dispose() { }
}
