using System.Text.RegularExpressions;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Lightweight, heuristic-based quality checks applied to translation output
/// before the result is returned to the caller.
///
/// Rules:
///  1. Omission guard   – translation must not be suspiciously shorter than source.
///  2. Numeric fidelity – every multi-digit number in source must appear in result.
///  3. Repetition guard – pathological token repetition (model collapse artefact).
///
/// On failure the caller can escalate to a higher-quality backend and retry.
/// </summary>
public static class TranslationQualityGuard
{
    // Minimum ratio of result chars to source chars for non-CJK→non-CJK pairs.
    private const double MinRatio = 0.10;

    // Maximum ratio before suspecting the model leaked the source text or hallucinated.
    private const double MaxRatio = 10.0;

    // Minimum absolute output length for any source longer than this threshold.
    private const int MinOutputCharsForLongSource = 10;
    private const int LongSourceThreshold = 40;

    // Regex: sequences of 2+ digits (currency, dates, stats, etc.)
    private static readonly Regex NumberPattern = new(@"\d{2,}", RegexOptions.Compiled);

    public static QualityCheckResult Check(
        string sourceText,
        string translation,
        string sourceLanguage,
        string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return QualityCheckResult.Fail("Empty translation output.");

        // Omission guard: severely short output for long input
        if (sourceText.Length >= LongSourceThreshold &&
            translation.Length < MinOutputCharsForLongSource)
        {
            return QualityCheckResult.Fail(
                $"Output too short ({translation.Length} chars) for source of {sourceText.Length} chars – likely truncation.");
        }

        // Length ratio guard (relaxed for CJK source/target pairs due to character density)
        if (!InvolvesCompressivePair(sourceLanguage, targetLanguage))
        {
            var ratio = (double)translation.Length / sourceText.Length;
            if (ratio < MinRatio)
                return QualityCheckResult.Fail(
                    $"Length ratio {ratio:F2} below minimum {MinRatio} – possible omission.");
            if (ratio > MaxRatio)
                return QualityCheckResult.Fail(
                    $"Length ratio {ratio:F2} above maximum {MaxRatio} – possible hallucination or source leak.");
        }

        // Numeric fidelity guard
        foreach (Match m in NumberPattern.Matches(sourceText))
        {
            if (!translation.Contains(m.Value, StringComparison.Ordinal))
                return QualityCheckResult.Fail(
                    $"Number '{m.Value}' from source not found in translation.");
        }

        // Sentence-count fidelity: when the source clearly ships ≥ 2 sentences,
        // the translation must preserve the sentence count. The pipeline already
        // segments per sentence before invoking the engine, so this is the last
        // line of defence against callers that bypass the segmenter – e.g.
        // "你好啊，胆小鬼。 你是不是不知道我是谁？" → "Hello, coward." (second clause dropped).
        var srcSentences = TextSegmenter.CountSentenceEndings(sourceText);
        if (srcSentences >= 2)
        {
            var tgtSentences = TextSegmenter.CountSentenceEndings(translation);
            if (tgtSentences < srcSentences)
                return QualityCheckResult.Fail(
                    $"Sentence count dropped from {srcSentences} to {tgtSentences} – likely omission of trailing clauses.");
        }

        // Repetition guard (model collapse: "the the the the …")
        if (HasPathologicalRepetition(translation))
            return QualityCheckResult.Fail("Pathological token repetition detected in output.");

        return QualityCheckResult.Pass();
    }

    /// <summary>
    /// Returns true for CJK↔Latin pairs where character counts are not
    /// directly comparable (e.g. "你好" → "Hello" changes length significantly).
    /// </summary>
    private static bool InvolvesCompressivePair(string src, string tgt)
    {
        var cjkLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh", "ja", "ko" };
        return cjkLangs.Contains(src) || cjkLangs.Contains(tgt);
    }

    private static bool HasPathologicalRepetition(string text)
    {
        // Check if any 3-char+ word appears more than 12 times consecutively via overlap.
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 12) return false;

        var maxRun = 1;
        var currentRun = 1;
        for (var i = 1; i < words.Length; i++)
        {
            currentRun = string.Equals(words[i], words[i - 1], StringComparison.Ordinal) ? currentRun + 1 : 1;
            maxRun = Math.Max(maxRun, currentRun);
        }
        return maxRun >= 12;
    }
}

public sealed record QualityCheckResult(bool IsAcceptable, string? FailureReason)
{
    public static QualityCheckResult Pass() => new(true, null);
    public static QualityCheckResult Fail(string reason) => new(false, reason);
}
