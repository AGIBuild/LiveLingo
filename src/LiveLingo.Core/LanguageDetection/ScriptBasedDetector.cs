using System.Globalization;
using System.Text;

namespace LiveLingo.Core.LanguageDetection;

/// <summary>
/// Lightweight language detector based on Unicode script distribution.
///
/// Confidence is dynamically computed from the dominant-script ratio:
///   ≥ 80 % → 0.95  (clearly single-script)
///   ≥ 60 % → 0.80  (dominant but with mixing)
///   ≥ 40 % → 0.60  (ambiguous, low confidence)
///        &lt; 40 % → 0.40  (highly mixed – treat as uncertain)
///
/// For Latin-script texts, a common-word frequency pass is used to
/// distinguish French, German, Spanish, Portuguese, and Italian from English.
/// </summary>
public sealed class ScriptBasedDetector : ILanguageDetector
{
    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (lang, confidence) = DetectWithConfidence(text);
        return Task.FromResult(new DetectionResult(lang, confidence));
    }

    public static (string Language, float Confidence) DetectWithConfidence(string text)
    {
        int cjk = 0, hiraganaKatakana = 0, hangul = 0, cyrillic = 0, arabic = 0, latin = 0, total = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsPunctuation(rune) || Rune.IsDigit(rune))
                continue;

            total++;
            var cat = Rune.GetUnicodeCategory(rune);
            var value = rune.Value;

            if (value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0x20000 and <= 0x2A6DF)
                cjk++;
            else if (value is >= 0x3040 and <= 0x30FF)
                hiraganaKatakana++;
            else if (value is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF)
                hangul++;
            else if (value is >= 0x0400 and <= 0x04FF)
                cyrillic++;
            else if (value is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F)
                arabic++;
            else if (cat is UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter
                     && value <= 0x024F)
                latin++;
        }

        if (total == 0) return ("en", 0.5f);

        // Hiragana/katakana presence is the strongest Japanese signal even in mixed-script text.
        if (hiraganaKatakana > 0)
        {
            var ratio = (float)(hiraganaKatakana + cjk) / total;
            return ("ja", ScriptConfidence(ratio));
        }

        // Check non-Latin scripts by dominance ratio.
        if (hangul * 2 > total) return ("ko", ScriptConfidence((float)hangul / total));
        if (cjk * 2 > total) return ("zh", ScriptConfidence((float)cjk / total));
        if (cyrillic * 2 > total) return ("ru", ScriptConfidence((float)cyrillic / total));
        if (arabic * 2 > total) return ("ar", ScriptConfidence((float)arabic / total));

        // Latin-dominant: use common-word frequency to distinguish European languages.
        if (latin * 2 > total)
        {
            var latinConfidence = ScriptConfidence((float)latin / total);
            var latin_lang = DetectLatinLanguage(text);
            return (latin_lang, latinConfidence);
        }

        // No clear dominant script – fall back to CJK hint or English, low confidence.
        var fallback = cjk > 0 ? "zh" : "en";
        return (fallback, 0.40f);
    }

    // Keep the old sync entry-point for internal / test use.
    public static string DetectByScript(string text) => DetectWithConfidence(text).Language;

    public void Dispose() { }

    private static float ScriptConfidence(float dominantRatio) => dominantRatio switch
    {
        >= 0.80f => 0.95f,
        >= 0.60f => 0.80f,
        >= 0.40f => 0.60f,
        _ => 0.40f
    };

    // ─── Latin language disambiguation ──────────────────────────────────────

    private static readonly (string Language, HashSet<string> Words)[] LatinHints =
    [
        ("fr", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "le", "la", "les", "de", "du", "un", "une", "et", "en", "que",
            "qui", "dans", "avec", "il", "elle", "nous", "vous", "ils", "est",
            "sont", "mais", "ou", "où", "comme", "par", "sur", "au" }),

        ("de", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "der", "die", "das", "und", "ist", "in", "ich", "zu", "von",
            "mit", "auf", "für", "an", "nicht", "auch", "aber", "oder",
            "ein", "eine", "den", "dem", "des", "wenn", "kann", "sein" }),

        ("es", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "el", "los", "las", "de", "que", "en", "un", "una", "del",
            "con", "por", "para", "más", "este", "esta", "pero", "como",
            "también", "ya", "hay", "tiene", "están", "puede", "todo" }),

        ("pt", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "de", "da", "do", "das", "dos", "que", "em", "um", "uma", "com",
            "para", "por", "não", "mais", "uma", "está", "são", "como",
            "isso", "esse", "ela", "eles", "também", "mas", "muito" }),

        ("it", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "il", "lo", "la", "gli", "le", "di", "del", "della", "che",
            "in", "un", "una", "con", "per", "su", "non", "mi", "si",
            "sono", "anche", "come", "ma", "quando", "questo", "tutto" }),
    ];

    private static string DetectLatinLanguage(string text)
    {
        // Tokenise by whitespace and strip punctuation from boundaries.
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '-'))
            .Where(w => w.Length >= 2)
            .ToList();

        if (words.Count == 0) return "en";

        var bestLang = "en";
        var bestScore = 0;

        foreach (var (lang, hints) in LatinHints)
        {
            var score = words.Count(w => hints.Contains(w));
            if (score > bestScore)
            {
                bestScore = score;
                bestLang = lang;
            }
        }

        // Require at least 2 matching function words to override the default "en".
        return bestScore >= 2 ? bestLang : "en";
    }
}
