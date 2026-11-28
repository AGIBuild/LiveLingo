using System.Globalization;
using System.Text.RegularExpressions;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// Deterministic validation for structured polish modes (<c>ai-task</c>, <c>formal</c>, <c>todo</c>, <c>report</c> and
/// structured custom presets). Unlike <see cref="FidelityGate"/>, it allows headings, Markdown lists, formal lexical
/// rewrites, Chinese-to-Arabic numeral normalization (e.g. "十八号" → "18号", "下午三点" → "15:00") and pruning of
/// non-actionable chatter while strictly blocking execution hijack, unrequested persona slop, invented numbers and
/// mutated protected literals.
/// </summary>
public static partial class FactAnchorGate
{
    private static readonly string[] NonNumericIdioms =
    [
        "一会儿", "一下子", "一定的", "一方面", "说一不二", "万一", "一旦",
        "一定", "一般", "一些", "一点", "一直", "一并", "一同", "一块",
        "一会", "一下", "一样", "一边", "一起", "一共", "一味", "一再", "一一"
    ];

    public static GateResult Check(
        string raw,
        string candidate,
        IReadOnlyList<string>? glossary = null,
        IReadOnlyList<GlossaryCorrection>? corrections = null,
        bool forbidExecution = false,
        bool allowSelectiveExtraction = false,
        bool ignoreBacktickLiterals = false)
    {
        return CheckDetailed(raw, candidate, glossary, corrections, forbidExecution, allowSelectiveExtraction, ignoreBacktickLiterals, out _);
    }

    public static GateResult CheckDetailed(
        string raw,
        string candidate,
        IReadOnlyList<string>? glossary,
        IReadOnlyList<GlossaryCorrection>? corrections,
        bool forbidExecution,
        bool allowSelectiveExtraction,
        bool ignoreBacktickLiterals,
        out bool isHardFailure)
    {
        isHardFailure = false;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            isHardFailure = true;
            return GateResult.Refuse(GateReason.Empty);
        }

        var trimmed = candidate.Trim();
        glossary ??= [];
        corrections ??= [];

        if (forbidExecution && HasHardSafetyViolation(raw, trimmed, ignoreBacktickLiterals, out var safetyReason))
        {
            isHardFailure = true;
            return GateResult.Refuse(safetyReason);
        }

        var rawNorm = FidelityGate.Normalize(raw);
        var candidateWithoutOrdinals = StripStructuralPrefixes(trimmed);
        var candNorm = FidelityGate.Normalize(candidateWithoutOrdinals);

        var activeCorrections = corrections
            .Where(c => c.Spoken.Length > 0 && c.Canonical.Length > 0
                        && rawNorm.Contains(FidelityGate.Normalize(c.Spoken), StringComparison.Ordinal)
                        && candNorm.Contains(FidelityGate.Normalize(c.Canonical), StringComparison.Ordinal))
            .ToList();

        var effectiveGlossary = glossary
            .Concat(activeCorrections.Select(c => c.Canonical))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var protectedSpans = ignoreBacktickLiterals
            ? TextUnits.NonCodeLiteralSpans(raw)
            : TextUnits.LiteralSpans(raw);
        foreach (var span in protectedSpans)
        {
            var literal = raw[span.Start..span.End];
            if (!trimmed.Contains(literal, StringComparison.Ordinal))
            {
                var inner = literal.Trim('`');
                if (inner.Length == 0 || !trimmed.Contains(inner, StringComparison.OrdinalIgnoreCase))
                {
                    isHardFailure = true;
                    return GateResult.Refuse(GateReason.TermLost, inner.Length > 0 ? inner : literal);
                }
            }
        }

        if (!allowSelectiveExtraction)
        {
            foreach (Match identifier in CodeIdentifierPattern().Matches(raw))
            {
                var sym = identifier.Value;
                if (trimmed.Contains(sym, StringComparison.OrdinalIgnoreCase)
                    || candNorm.Contains(FidelityGate.Normalize(sym), StringComparison.Ordinal))
                    continue;
                if (activeCorrections.Any(c => c.Spoken.Equals(sym, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (FidelityGate.HasRetractionLookahead(raw, identifier))
                    continue;
                return GateResult.Refuse(GateReason.TermLost, sym);
            }
        }

        var candidateNumbers = ExtractCanonicalNumberSet(candidateWithoutOrdinals, includeClockAliases: false);
        if (!allowSelectiveExtraction)
        {
            var rawDigits = DigitRun().Matches(raw).Cast<Match>().ToList();
            var nonRetractedDigitCount = rawDigits.Count(m => !FidelityGate.HasRetractionLookahead(raw, m));
            foreach (var digits in rawDigits)
            {
                var canon = CanonicalDigits(digits.Value);
                if (candNorm.Contains(digits.Value, StringComparison.Ordinal) || candidateNumbers.Contains(canon))
                    continue;
                if (activeCorrections.Any(c => FidelityGate.Normalize(c.Spoken).Contains(digits.Value, StringComparison.Ordinal)))
                    continue;
                if (FidelityGate.IsPrunableSpokenNumber(raw, digits, nonRetractedDigitCount))
                    continue;
                return GateResult.Refuse(GateReason.NumberLost, digits.Value);
            }
        }

        var allowedFromRaw = ExtractCanonicalNumberSet(raw, includeClockAliases: true);
        foreach (var g in effectiveGlossary)
        {
            foreach (var n in ExtractCanonicalNumberSet(g, includeClockAliases: false))
                allowedFromRaw.Add(n);
        }

        foreach (Match digits in DigitRun().Matches(candidateWithoutOrdinals))
        {
            var canon = CanonicalDigits(digits.Value);
            if (rawNorm.Contains(digits.Value, StringComparison.Ordinal) || allowedFromRaw.Contains(canon))
                continue;
            if (effectiveGlossary.Any(g => FidelityGate.Normalize(g).Contains(digits.Value, StringComparison.Ordinal)))
                continue;
            return GateResult.Refuse(GateReason.NumberInvented, digits.Value);
        }

        return GateResult.Accept;
    }

    private static string StripStructuralPrefixes(string text) =>
        MarkdownHeadingPrefix().Replace(ListOrdinalPrefix().Replace(text, ""), "");

    private static string CanonicalDigits(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    internal static HashSet<string> ExtractCanonicalNumberSet(string text, bool includeClockAliases)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
            return set;

        foreach (Match m in DigitRun().Matches(text))
            set.Add(CanonicalDigits(m.Value));

        var masked = text;
        foreach (var idiom in NonNumericIdioms)
            masked = masked.Replace(idiom, "  ", StringComparison.Ordinal);

        foreach (Match m in ChineseNumeralRun().Matches(masked))
        {
            if (TryParseChineseNumeral(m.Value, out var value))
                set.Add(value.ToString(CultureInfo.InvariantCulture));
        }

        if (includeClockAliases)
        {
            // Spoken clock times ("下午三点" -> 15:00, "晚上十点" -> 22:00, "八点" -> 8:00, "点半" -> 30).
            foreach (Match m in PmClockPattern().Matches(text))
            {
                if (TryParseClockHour(m.Groups[1].Value, out var hour) && hour is >= 1 and <= 11)
                {
                    set.Add((hour + 12).ToString(CultureInfo.InvariantCulture));
                    set.Add("0");
                }
            }
            foreach (Match _ in SpokenHourPattern().Matches(text))
                set.Add("0");
            if (text.Contains("半", StringComparison.Ordinal))
            {
                set.Add("30");
                set.Add("5");
            }
            if (text.Contains("一刻", StringComparison.Ordinal))
                set.Add("15");
            if (text.Contains("三刻", StringComparison.Ordinal))
                set.Add("45");

            // Spoken two-digit calendar years ("零九年" -> 2009, "二四年" -> 2024, "九八年" -> 1998, "09年" -> 2009).
            // Pure digit-pair syntax excludes "十七年" (duration of 17 years, which contains '十').
            foreach (Match m in SpokenTwoDigitChineseYearPattern().Matches(text))
            {
                var d1 = ChineseDigitValue(m.Groups[1].Value[0]);
                var d2 = ChineseDigitValue(m.Groups[2].Value[0]);
                if (d1 >= 0 && d2 >= 0)
                {
                    var yy = d1 * 10 + d2;
                    set.Add((yy <= 39 ? 2000 + yy : 1900 + yy).ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (Match m in SpokenTwoDigitArabicYearPattern().Matches(text))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var yy))
                    set.Add((yy <= 39 ? 2000 + yy : 1900 + yy).ToString(CultureInfo.InvariantCulture));
            }

            // Chinese percentage/tenth expressions ("九成五" -> 95, "八成" -> 80, "七成半" -> 75).
            foreach (Match m in ChengRatioPattern().Matches(text))
            {
                var tens = ChineseDigitValue(m.Groups[1].Value[0]);
                if (tens <= 0)
                    continue;
                var units = 0;
                if (m.Groups[2].Success)
                {
                    var uChar = m.Groups[2].Value[0];
                    units = uChar == '半' ? 5 : Math.Max(0, ChineseDigitValue(uChar));
                }
                set.Add((tens * 10 + units).ToString(CultureInfo.InvariantCulture));
            }

            // Explicit Chinese fractions ("五分之四" -> 80%) and spoken trial counts ("跑五次，有四次" -> 80%).
            foreach (Match m in ChineseFractionPattern().Matches(text))
            {
                if (TryParseClockHour(m.Groups[1].Value, out var denom)
                    && TryParseClockHour(m.Groups[2].Value, out var num)
                    && denom > 0 && num >= 0 && num <= denom * 10)
                {
                    set.Add(((long)Math.Round(num * 100.0 / denom)).ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (Match m in SpokenTrialRatioPattern().Matches(text))
            {
                if (TryParseClockHour(m.Groups[1].Value, out var denom)
                    && TryParseClockHour(m.Groups[2].Value, out var num)
                    && denom is >= 2 and <= 100 && num >= 1 && num <= denom)
                {
                    set.Add(((long)Math.Round(num * 100.0 / denom)).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return set;
    }

    private static bool TryParseClockHour(string token, out long hour) =>
        long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out hour)
        || TryParseChineseNumeral(token, out hour);

    internal static bool TryParseChineseNumeral(ReadOnlySpan<char> span, out long result)
    {
        result = 0;
        if (span.IsEmpty)
            return false;

        // Pure digit-by-digit sequence such as "二零二六" / "幺零二四".
        var hasUnit = false;
        foreach (var c in span)
        {
            if (c is '十' or '百' or '千' or '万' or '亿')
            {
                hasUnit = true;
                break;
            }
        }

        if (!hasUnit)
        {
            long direct = 0;
            foreach (var c in span)
            {
                var d = ChineseDigitValue(c);
                if (d < 0)
                    return false;
                direct = direct * 10 + d;
            }
            result = direct;
            return true;
        }

        long total = 0;
        long section = 0;
        long currentDigit = 0;
        var hasDigit = false;

        foreach (var c in span)
        {
            var digit = ChineseDigitValue(c);
            if (digit >= 0)
            {
                currentDigit = digit;
                hasDigit = true;
                continue;
            }

            switch (c)
            {
                case '十':
                    section += (hasDigit ? currentDigit : 1) * 10;
                    currentDigit = 0;
                    hasDigit = false;
                    break;
                case '百':
                    section += (hasDigit ? currentDigit : 1) * 100;
                    currentDigit = 0;
                    hasDigit = false;
                    break;
                case '千':
                    section += (hasDigit ? currentDigit : 1) * 1000;
                    currentDigit = 0;
                    hasDigit = false;
                    break;
                case '万':
                    section += currentDigit;
                    total += (section == 0 ? 1 : section) * 10_000;
                    section = 0;
                    currentDigit = 0;
                    hasDigit = false;
                    break;
                case '亿':
                    section += currentDigit;
                    total = (total + (section == 0 ? 1 : section)) * 100_000_000;
                    section = 0;
                    currentDigit = 0;
                    hasDigit = false;
                    break;
                default:
                    return false;
            }
        }

        result = total + section + currentDigit;
        return true;
    }

    private static int ChineseDigitValue(char c) => c switch
    {
        '零' or '〇' => 0,
        '一' or '幺' => 1,
        '二' or '两' => 2,
        '三' => 3,
        '四' => 4,
        '五' => 5,
        '六' => 6,
        '七' => 7,
        '八' => 8,
        '九' => 9,
        _ => -1
    };

    internal static bool HasHardSafetyViolation(
        string raw,
        string trimmedCandidate,
        bool ignoreBacktickLiterals,
        out GateReason reason)
    {
        if (HijackPreamble().IsMatch(trimmedCandidate))
        {
            reason = GateReason.ExecutionHijack;
            return true;
        }

        if ((ignoreBacktickLiterals || !raw.Contains("```", StringComparison.Ordinal)) && FencedCodeBlock().IsMatch(trimmedCandidate))
        {
            reason = GateReason.ExecutionHijack;
            return true;
        }

        if (!RoleplayPattern().IsMatch(raw) && RoleplayPattern().IsMatch(trimmedCandidate))
        {
            reason = GateReason.RoleplaySlop;
            return true;
        }

        reason = default;
        return false;
    }

    internal static bool IsCodeIdentifier(string token) => CodeIdentifierPattern().IsMatch(token);

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    // Matches multi-case code symbols (RefreshAsync, UserSessionStore, HTTPClient), snake_case identifiers (max_tokens), and dotted filenames (login.ts).
    [GeneratedRegex(@"\b(?:[A-Za-z]+[a-z0-9]+[A-Z][A-Za-z0-9]*|[A-Z]{2,}[a-z]+[A-Za-z0-9]*|[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+|[A-Za-z0-9_-]+\.(?:ts|tsx|js|jsx|cs|py|go|rs|java|json|yaml|yml|md|sql|sh|css|html))\b", RegexOptions.CultureInvariant)]
    private static partial Regex CodeIdentifierPattern();

    [GeneratedRegex(@"[零〇一二两三四五六七八九十百千万亿幺]+", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseNumeralRun();

    [GeneratedRegex(@"(?:下午|晚上|傍晚|今晚|明晚)\s*(\d+|[一二两三四五六七八九十]{1,3})\s*[点时]", RegexOptions.CultureInvariant)]
    private static partial Regex PmClockPattern();

    [GeneratedRegex(@"(?:\d+|[一二两三四五六七八九十两]{1,3})\s*[点时]", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenHourPattern();

    [GeneratedRegex(@"([零〇一二三四五六七八九幺])([零〇一二三四五六七八九幺])\s*年", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenTwoDigitChineseYearPattern();

    [GeneratedRegex(@"(?<!\d)(\d{2})\s*年", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenTwoDigitArabicYearPattern();

    [GeneratedRegex(@"([一二两三四五六七八九])\s*成\s*([一二三四五六七八九半])?", RegexOptions.CultureInvariant)]
    private static partial Regex ChengRatioPattern();

    [GeneratedRegex(@"(\d+|[一二两三四五六七八九十百]{1,4})\s*分之\s*(\d+|[一二两三四五六七八九十百]{1,4})", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseFractionPattern();

    [GeneratedRegex(@"(\d+|[一二两三四五六七八九十]{1,3})\s*(?:次|个|回|轮|单|项)[^。！？!?\n]{0,18}?有\s*(\d+|[一二两三四五六七八九十]{1,3})\s*(?:次|个|回|轮|单|项)", RegexOptions.CultureInvariant)]
    private static partial Regex SpokenTrialRatioPattern();

    [GeneratedRegex(@"^\s*\d+[.)、]\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ListOrdinalPrefix();

    [GeneratedRegex(@"^\s*#{1,6}\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownHeadingPrefix();

    [GeneratedRegex(@"^\s*(?:(?:好的|当然可以|没问题|明白了)[，,！!：:\s]*(?:下面|以下|这是|我来|为您|为你)|(?:Sure|Certainly|Of course|Here is|Below is)\b[^.\n]*:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HijackPreamble();

    [GeneratedRegex(@"```[A-Za-z0-9_+-]*\s*\n", RegexOptions.CultureInvariant)]
    private static partial Regex FencedCodeBlock();

    [GeneratedRegex(@"(?:你是一位|你是一个|作为一名)[^，。\n]{0,20}(?:资深|专家|大师|架构师)", RegexOptions.CultureInvariant)]
    private static partial Regex RoleplayPattern();
}
