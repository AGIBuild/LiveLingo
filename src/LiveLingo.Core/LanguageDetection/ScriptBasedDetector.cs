using System.Globalization;
using System.Text;

namespace LiveLingo.Core.LanguageDetection;

public sealed class ScriptBasedDetector : ILanguageDetector
{
    public Task<DetectionResult> DetectAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var lang = DetectByScript(text);
        return Task.FromResult(new DetectionResult(lang, 0.8f));
    }

    public static string DetectByScript(string text)
    {
        int cjk = 0, hiraganaKatakana = 0, hangul = 0, cyrillic = 0, arabic = 0, latin = 0, total = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsPunctuation(rune))
                continue;

            total++;
            var cat = Rune.GetUnicodeCategory(rune);
            var value = rune.Value;

            if (value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF)
                cjk++;
            else if (value is >= 0x3040 and <= 0x30FF)
                hiraganaKatakana++;
            else if (value is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF)
                hangul++;
            else if (value is >= 0x0400 and <= 0x04FF)
                cyrillic++;
            else if (value is >= 0x0600 and <= 0x06FF)
                arabic++;
            else if (cat is UnicodeCategory.LowercaseLetter or UnicodeCategory.UppercaseLetter
                     && value <= 0x024F)
                latin++;
        }

        if (total == 0) return "en";

        if (hiraganaKatakana > 0) return "ja";
        if (hangul * 2 > total) return "ko";
        if (cjk * 2 > total) return "zh";
        if (cyrillic * 2 > total) return "ru";
        if (arabic * 2 > total) return "ar";
        if (latin * 2 > total) return "en";

        return cjk > 0 ? "zh" : "en";
    }

    public void Dispose() { }
}
