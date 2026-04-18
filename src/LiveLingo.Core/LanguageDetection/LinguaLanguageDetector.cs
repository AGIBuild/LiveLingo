using Lingua;

namespace LiveLingo.Core.LanguageDetection;

/// <summary>
/// Statistical language detector backed by <c>SearchPioneer.Lingua</c>.
/// Uses rule-based filters plus n-gram language models, constrained to the
/// 10 languages the translation pipeline supports to keep memory footprint
/// and cold-start time small.
///
/// Language models are lazy-loaded on first detection call by design of the
/// underlying library; subsequent calls hit in-memory models.
/// </summary>
public sealed class LinguaLanguageDetector : ILanguageDetector
{
    // Translation pipeline's supported set, mapped to Lingua Language enum values.
    private static readonly IReadOnlyDictionary<Language, string> LanguageCodes =
        new Dictionary<Language, string>
        {
            [Language.Chinese] = "zh",
            [Language.English] = "en",
            [Language.Japanese] = "ja",
            [Language.Korean] = "ko",
            [Language.French] = "fr",
            [Language.German] = "de",
            [Language.Spanish] = "es",
            [Language.Russian] = "ru",
            [Language.Arabic] = "ar",
            [Language.Portuguese] = "pt",
        };

    private readonly LanguageDetector _detector;

    public LinguaLanguageDetector()
    {
        _detector = LanguageDetectorBuilder
            .FromLanguages(LanguageCodes.Keys.ToArray())
            .Build();
    }

    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new DetectionResult("en", 0.5f));

        var confidences = _detector.ComputeLanguageConfidenceValues(text).ToList();
        if (confidences.Count == 0)
            return Task.FromResult(new DetectionResult("en", 0.4f));

        var top = confidences[0];
        var code = LanguageCodes.TryGetValue(top.Key, out var isoCode) ? isoCode : "en";
        return Task.FromResult(new DetectionResult(code, (float)top.Value));
    }

    public void Dispose() { }
}
