namespace LiveLingo.Core.LanguageDetection;

public interface ILanguageDetector : IDisposable
{
    Task<DetectionResult> DetectAsync(
        string text,
        CancellationToken ct = default);
}
