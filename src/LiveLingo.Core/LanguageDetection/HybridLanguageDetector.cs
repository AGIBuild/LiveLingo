using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.LanguageDetection;

/// <summary>
/// Two-stage language detector.
///
/// Stage 1 – <see cref="ScriptBasedDetector"/>: near-zero-cost Unicode-script
/// heuristic. For non-Latin, single-script inputs (Chinese, Japanese, Korean,
/// Cyrillic, Arabic) this gives a highly reliable answer, so we return early
/// and avoid loading statistical models.
///
/// Stage 2 – <see cref="LinguaLanguageDetector"/>: n-gram statistical models
/// constrained to the pipeline's 10 supported languages. Invoked only when
/// the script-based result is ambiguous (Latin-dominant text, mixed scripts,
/// or low script-dominance ratio), which is exactly where a rule engine
/// plus n-gram classifier outperforms a pure script heuristic.
///
/// Routing is a capability-based dispatch, not a defensive fallback: the
/// script detector is the right tool for some classes of input, and the
/// statistical detector is the right tool for others.
/// </summary>
public sealed class HybridLanguageDetector : ILanguageDetector
{
    private const float ScriptConfidenceThreshold = 0.8f;

    private static readonly HashSet<string> NonLatinSingleScriptLanguages =
        new(StringComparer.Ordinal) { "zh", "ja", "ko", "ru", "ar" };

    private readonly ILanguageDetector _scriptDetector;
    private readonly ILanguageDetector _statisticalDetector;
    private readonly ILogger<HybridLanguageDetector> _logger;

    public HybridLanguageDetector(
        ScriptBasedDetector scriptDetector,
        LinguaLanguageDetector statisticalDetector,
        ILogger<HybridLanguageDetector> logger)
    {
        _scriptDetector = scriptDetector;
        _statisticalDetector = statisticalDetector;
        _logger = logger;
    }

    public async Task<DetectionResult> DetectAsync(string text, CancellationToken ct = default)
    {
        var script = await _scriptDetector.DetectAsync(text, ct).ConfigureAwait(false);

        if (IsHighConfidenceNonLatin(script))
        {
            _logger.LogDebug(
                "Language detection: script stage decisive ({Language}, conf={Confidence:F2})",
                script.Language, script.Confidence);
            return script;
        }

        var statistical = await _statisticalDetector.DetectAsync(text, ct).ConfigureAwait(false);
        _logger.LogDebug(
            "Language detection: script={ScriptLang}/{ScriptConf:F2} → statistical={StatLang}/{StatConf:F2}",
            script.Language, script.Confidence, statistical.Language, statistical.Confidence);

        return statistical;
    }

    private static bool IsHighConfidenceNonLatin(DetectionResult script) =>
        script.Confidence >= ScriptConfidenceThreshold
        && NonLatinSingleScriptLanguages.Contains(script.Language);

    public void Dispose()
    {
        _scriptDetector.Dispose();
        _statisticalDetector.Dispose();
    }
}
