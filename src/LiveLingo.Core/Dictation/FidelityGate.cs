using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Dictation;

public readonly record struct GateResult(GateRejection? Rejection)
{
    public bool Accepted => Rejection is null;

    public static GateResult Accept => default;

    public static GateResult Refuse(GateReason reason, string detail = "") => new(new GateRejection(reason, detail));
}

/// <summary>
/// Deterministic checks that reject a cleanup result that is not a faithful edit. They do not prove semantic fidelity;
/// they block the failures observed with small local models: dropped or silently deleted content, invented content,
/// invented numbers, and lost or invented technical terms.
///
/// Chinese text is compared character by character. English words are judged separately, because a glossary
/// legitimately turns phonetic gibberish into a long identifier and must not look like "added length".
/// Every deletion must be explainable: a filler word, a repetition, a self-correction, a single noise
/// character, or garbage that a glossary term replaced. Anything else is information the user said, so the edit is refused.
/// The text may be a whole spoken passage: a clause the speaker restated is then dropped because the cleaned text still says it.
/// </summary>
public static partial class FidelityGate
{
    public const double MinLengthRatio = 0.55;
    public const double MaxLengthRatio = 1.2;

    /// <summary>A self-correction ("设成十秒，不对，设成三十秒") may remove this many characters.</summary>
    public const int MaxCorrectionRun = 12;

    /// <summary>Characters of garbage one glossary replacement may remove.</summary>
    public const int GlossaryDropPerReplacement = 6;

    private static readonly string[] Fillers =
    [
        "怎么说呢", "然后呢", "所以说", "对吧", "你看", "那个", "这个", "就是", "的话", "那么", "然后", "其实", "一下",
        "呃", "嗯", "啊", "哦", "额", "呢", "呀", "吧", "嘛"
    ];
    private static readonly string[] CorrectionMarkers = ["不对", "不是", "说错了", "错了", "我是说", "应该是", "改成", "更正"];

    /// <summary>Phrases that only ever announce a self-correction; "不是" or "改成" also occur in ordinary statements.</summary>
    private static readonly string[] SpokenCorrections = ["不对", "说错了", "我是说", "更正"];

    /// <summary>Markers within a short lookahead window that retract an immediately preceding spoken number.</summary>
    private static readonly string[] NumberRetractionMarkers =
        ["不对", "说错了", "我是说", "口误", "算了", "改成", "改为", "更正", "干脆关掉", "取消"];

    /// <summary>Contrastive negation prefixes ("设为 30 秒，不是 15 分钟") that mark a secondary number as a rejected contrast.</summary>
    private static readonly string[] ContrastNegationPrefixes = ["不是", "而非", "不要设成", "别设成", "切勿设为"];

    /// <summary>Fixed lexical words containing "一" that express degree, aspect or manner rather than a counted quantity.</summary>
    private static readonly string[] NonNumericOneWords =
    [
        "一会儿", "一下子", "一定的", "一方面",
        "一定", "一般", "一些", "一点", "一直", "一旦", "一并", "一同", "一块",
        "一会", "一下", "一样", "一边", "一起", "一共", "一味", "一再"
    ];

    /// <summary>A deleted run counts as restated when this share of its character pairs still occurs in the cleaned text.</summary>
    private const double RestatedCoverage = 0.6;

    private const string Negations = "不没别无非未否";

    /// <summary>Lexical compounds where a character in <see cref="Negations"/> carries a non-negation reading (e.g. "没收" mòshōu, "识别", "非常").</summary>
    private static readonly string[] NonNegationCompounds =
    [
        "没收", "淹没", "沉没", "出没", "埋没", "覆没", "湮没", "辱没",
        "别人", "别处", "别的", "区别", "分别", "识别", "特别", "级别", "类别", "个别", "告别",
        "非常", "非凡"
    ];

    public static GateResult Check(
        string raw,
        string cleaned,
        IReadOnlyList<string>? glossary = null,
        bool allowParagraphs = false,
        IReadOnlyList<GlossaryCorrection>? corrections = null,
        bool relaxedVerbatim = false,
        bool spokenDocument = false)
    {
        if (string.IsNullOrWhiteSpace(cleaned))
            return GateResult.Refuse(GateReason.Empty);

        // One line in, one line out. A line break means the model echoed the prompt layout ("词表：… 目标：…").
        if (!allowParagraphs && cleaned.Contains('\n') && !raw.Contains('\n'))
            return GateResult.Refuse(GateReason.MultiLine);

        if (spokenDocument)
        {
            if (!raw.Contains('#') && InventedMarkdownHeading().IsMatch(cleaned))
                return GateResult.Refuse(GateReason.Added, "markdown");
            if (!raw.Contains("[ ]", StringComparison.Ordinal) && InventedTaskCheckbox().IsMatch(cleaned))
                return GateResult.Refuse(GateReason.Added, "markdown");
        }

        foreach (var (open, close) in QuotePairs)
            if (Balance(raw, open, close) != Balance(cleaned, open, close))
                return GateResult.Refuse(GateReason.Brackets);

        var rawSanitized = TextTidy.StripMalformedTokens(raw);
        var rawNorm = Normalize(rawSanitized);
        var cleanedNorm = Normalize(cleaned);
        glossary ??= [];
        corrections ??= [];

        var activeCorrections = corrections
            .Where(c => c.Spoken.Length > 0 && c.Canonical.Length > 0
                        && rawNorm.Contains(Normalize(c.Spoken), StringComparison.Ordinal)
                        && cleanedNorm.Contains(Normalize(c.Canonical), StringComparison.Ordinal))
            .ToList();

        var effectiveGlossary = glossary
            .Concat(activeCorrections.Select(c => c.Canonical))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var glossaryNorm = effectiveGlossary.Select(Normalize).Where(g => g.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        var digitMatches = DigitRun().Matches(rawSanitized).Cast<Match>().ToList();
        var nonRetractedDigitCount = digitMatches.Count(m => !HasRetractionLookahead(rawSanitized, m));
        foreach (var digits in digitMatches)
        {
            if (cleanedNorm.Contains(digits.Value, StringComparison.Ordinal))
                continue;
            if (activeCorrections.Any(c => Normalize(c.Spoken).Contains(digits.Value, StringComparison.Ordinal)))
                continue;
            if (IsPrunableSpokenNumber(rawSanitized, digits, nonRetractedDigitCount))
                continue;
            return GateResult.Refuse(GateReason.NumberLost, digits.Value);
        }

        // Numbers are never invented: turning garbled Chinese numerals into digits is a guess, so it is left to the recognizer.
        foreach (Match digits in DigitRun().Matches(cleaned))
        {
            if (rawNorm.Contains(digits.Value, StringComparison.Ordinal))
                continue;
            if (effectiveGlossary.Any(g => Normalize(g).Contains(digits.Value, StringComparison.Ordinal)))
                continue;
            return GateResult.Refuse(GateReason.NumberInvented, digits.Value);
        }

        // An English word the cleaner removed is acceptable when a glossary term, an explicit accent correction,
        // or a phonetically close English term already spoken elsewhere in the same dictation took its place.
        var correctedSpokenNorms = activeCorrections.Select(c => Normalize(c.Spoken)).Where(s => s.Length > 0).ToList();
        var lostRuns = AsciiRun().Matches(rawSanitized)
            .Where(run => !IsCorrectedAway(rawSanitized, run))
            .Where(run => !spokenDocument || IsProtectedSpokenAsciiRun(run.Value, effectiveGlossary, glossaryNorm))
            .Select(run => Words(run.Value).FirstOrDefault(w =>
                !cleanedNorm.Contains(w, StringComparison.Ordinal)
                && !correctedSpokenNorms.Any(s => s.Contains(w, StringComparison.Ordinal))))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();

        var glossaryGains = CountGainedGlossaryTerms(rawSanitized, cleaned, effectiveGlossary);
        var directGlossaryRepairs = CountDirectGlossaryPhoneticRepairs(lostRuns, cleanedNorm, effectiveGlossary);
        var contextRepairs = CountInContextEnglishRepairs(rawSanitized, cleaned, lostRuns);
        var replacements = Math.Max(glossaryGains, directGlossaryRepairs) + activeCorrections.Count;
        if (lostRuns.Count > replacements + contextRepairs)
            return GateResult.Refuse(GateReason.TermLost, lostRuns[0]);

        // An English word the cleaner introduced must come from the original speech or the glossary/corrections.
        var entityAllowance = spokenDocument ? 2 : 0;
        foreach (var word in Words(cleaned))
        {
            if (rawNorm.Contains(word, StringComparison.Ordinal) || glossaryNorm.Any(g => g.Contains(word, StringComparison.Ordinal)))
                continue;
            if (entityAllowance > 0)
            {
                entityAllowance--;
                continue;
            }
            return GateResult.Refuse(GateReason.TermNotInGlossary, word);
        }

        var rawCjk = Cjk(rawSanitized);
        var cleanedCjk = Cjk(cleaned);
        if (rawCjk.Length == 0) return GateResult.Accept;

        var ratio = (double)cleanedCjk.Length / rawCjk.Length;
        var shown = (ratio * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";
        var minRatio = spokenDocument ? 0.40 : relaxedVerbatim ? 0.45 : MinLengthRatio;
        var maxRatio = relaxedVerbatim || spokenDocument ? 1.35 : MaxLengthRatio;
        if (ratio < minRatio && activeCorrections.Count == 0 && glossaryGains == 0)
            return GateResult.Refuse(GateReason.TooShort, shown);
        if (ratio < 0.35)
            return GateResult.Refuse(GateReason.TooShort, shown);
        if (ratio > maxRatio) return GateResult.Refuse(GateReason.TooLong, shown);

        var (dropped, added, addedRuns) = Align(rawCjk, cleanedCjk);
        var correctionAddedBudget = activeCorrections.Sum(c => Cjk(c.Canonical).Length);
        var addShare = relaxedVerbatim || spokenDocument ? cleanedCjk.Length / 3 : cleanedCjk.Length / 5;
        if (added > Math.Max(6, addShare) + correctionAddedBudget)
            return GateResult.Refuse(GateReason.Added, added.ToString(CultureInfo.InvariantCulture));

        foreach (var (cleanedStart, length, rawAnchor) in addedRuns)
        {
            var addedSpan = cleanedCjk.Substring(cleanedStart, length);
            foreach (var ch in addedSpan)
            {
                if (!Negations.Contains(ch))
                    continue;
                if (ch == '否' && IsInterrogativeShifouAddition(rawCjk, rawAnchor, cleanedCjk, cleanedStart, length))
                    continue;
                if (effectiveGlossary.Any(g => Cjk(g).Contains(ch)))
                    continue;
                return GateResult.Refuse(GateReason.Added, addedSpan);
            }
        }

        var garbageBudget = glossaryGains * GlossaryDropPerReplacement
                            + activeCorrections.Sum(c => Math.Max(GlossaryDropPerReplacement, Cjk(c.Spoken).Length));
        var dropShare = spokenDocument ? 0.28 : relaxedVerbatim ? 0.18 : SmallDropShare;
        var maxSmall = spokenDocument ? 8 : relaxedVerbatim ? 5 : MaxSmallDrop;
        var slack = Math.Max(MinSmallDrops, (int)(rawCjk.Length * dropShare));
        foreach (var (start, length) in dropped)
        {
            if (IsExplainedDeletion(rawCjk, start, length, cleanedCjk)) continue;
            if (length <= garbageBudget)
            {
                garbageBudget -= length;
                continue;
            }
            var small = SmallDrop(rawCjk, start, length, maxSmall);
            if (small > 0 && small <= slack)
            {
                slack -= small;
                continue;
            }
            return GateResult.Refuse(GateReason.Deleted, rawCjk.Substring(start, length));
        }

        return GateResult.Accept;
    }

    /// <summary>
    /// Polishing a spoken passage always trims a few light words around the fillers ("他们的这个工作" to "工作"): a pronoun,
    /// a measure word. A drop of at most this many characters, without a negation, is tolerated up to a share of the text.
    /// </summary>
    private const int MaxSmallDrop = 3;

    private const int MinSmallDrops = 4;
    private const double SmallDropShare = 0.08;

    /// <summary>The characters of a short deletion that is no filler and holds no negation or spoken number; 0 when it is not a small drop.</summary>
    private static int SmallDrop(string raw, int start, int length, int maxLen = MaxSmallDrop)
    {
        if (HasActiveNegation(raw, start, length))
            return 0;

        var rest = WithoutFillers(raw.Substring(start, length));
        if (rest.Length <= 0 || rest.Length > maxLen)
            return 0;

        var digitCheck = WithoutNonNumericOneWords(rest);
        return !digitCheck.Any(c => SpokenDigits.Contains(c)) ? rest.Length : 0;
    }

    private static bool HasActiveNegation(string text, int start, int length)
    {
        for (var idx = start; idx < start + length; idx++)
        {
            if (!Negations.Contains(text[idx]))
                continue;
            var leftCompound = idx > 0 && NonNegationCompounds.Any(w => w.Length == 2 && w[0] == text[idx - 1] && w[1] == text[idx]);
            var rightCompound = idx + 1 < text.Length && NonNegationCompounds.Any(w => w.Length == 2 && w[0] == text[idx] && w[1] == text[idx + 1]);
            if (!leftCompound && !rightCompound)
                return true;
        }
        return false;
    }

    private static string WithoutNonNumericOneWords(string text)
    {
        foreach (var idiom in NonNumericOneWords)
            text = text.Replace(idiom, "", StringComparison.Ordinal);
        return text;
    }

    /// <summary>Chinese numerals: a trim that removes one changes a number the digit check cannot see.</summary>
    private const string SpokenDigits = "零一二三四五六七八九十百千万亿两半";

    private static readonly (char Open, char Close)[] QuotePairs =
        [('“', '”'), ('‘', '’'), ('「', '」'), ('『', '』'), ('（', '）'), ('(', ')'), ('《', '》')];

    private static int Balance(string text, char open, char close) => text.Count(c => c == open) - text.Count(c => c == close);

    private static bool IsExplainedDeletion(string raw, int start, int length, string cleaned)
    {
        var text = raw.Substring(start, length);
        if (IsInterrogativeShifouRewrite(raw, start, length, cleaned)) return true;
        var activeNegation = HasActiveNegation(raw, start, length);
        if (length <= 1) return !activeNegation;

        var rest = WithoutFillers(text);
        if (rest.Length <= 1 && !activeNegation) return true;
        if (IsRepetition(raw, start, length)) return true;
        if (length <= MaxCorrectionRun && CorrectionMarkers.Any(m => text.Contains(m, StringComparison.Ordinal))) return true;
        return IsRestated(rest, cleaned, activeNegation);
    }

    /// <summary>
    /// Rewriting a spoken A-not-A question ("会不会" / "是不是" / "能不能" / "有没有") to "是否" preserves neutral
    /// interrogative polarity; character LCS otherwise sees "不"/"没" dropped and "否" added separately.
    /// </summary>
    private static bool IsInterrogativeShifouRewrite(string raw, int start, int length, string cleaned)
    {
        if (length > 4 || !cleaned.Contains("是否", StringComparison.Ordinal))
            return false;

        var windowStart = Math.Max(0, start - 2);
        var windowEnd = Math.Min(raw.Length, start + length + 2);
        var window = raw.AsSpan(windowStart, windowEnd - windowStart);
        if (window.Contains("有没有", StringComparison.Ordinal))
            return true;

        for (var i = 0; i + 2 < window.Length; i++)
        {
            if (window[i + 1] == '不' && window[i] == window[i + 2] && window[i] != '不')
                return true;
        }
        return false;
    }

    private static bool IsInterrogativeShifouAddition(string raw, int rawAnchor, string cleaned, int cleanedStart, int length)
    {
        var cStart = Math.Max(0, cleanedStart - 1);
        var cEnd = Math.Min(cleaned.Length, cleanedStart + length + 1);
        if (!cleaned.AsSpan(cStart, cEnd - cStart).Contains("是否", StringComparison.Ordinal))
            return false;

        var rStart = Math.Max(0, rawAnchor - 4);
        var rEnd = Math.Min(raw.Length, rawAnchor + 4);
        var window = raw.AsSpan(rStart, rEnd - rStart);
        if (window.Contains("有没有", StringComparison.Ordinal))
            return true;

        for (var i = 0; i + 2 < window.Length; i++)
        {
            if (window[i + 1] == '不' && window[i] == window[i + 2] && window[i] != '不')
                return true;
        }
        return false;
    }

    private static string WithoutFillers(string text)
    {
        foreach (var filler in Fillers) text = text.Replace(filler, "", StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// The deleted text says again what the cleaned text still says ("准确率很高。准确率不是很高"), so nothing is lost.
    /// A deleted negation is only excused when its exact 2-character lexical context (such as "能不能" in a repeated
    /// stuttered question) still stands intact in <paramref name="cleaned"/>.
    /// </summary>
    private static bool IsRestated(string text, string cleaned, bool activeNegation)
    {
        if (activeNegation && !AreNegationsPreservedInContext(text, cleaned))
            return false;

        var pairs = text.Length - 1;
        if (pairs <= 0)
            return false;
        var covered = 0;
        for (var i = 0; i < pairs; i++)
            if (cleaned.Contains(text.AsSpan(i, 2), StringComparison.Ordinal))
                covered++;
        return covered >= pairs * RestatedCoverage;
    }

    private static bool AreNegationsPreservedInContext(string text, string cleaned)
    {
        if (text.Length < 4)
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (!Negations.Contains(text[i]))
                continue;

            var hasRight = i + 1 < text.Length && cleaned.Contains(text.AsSpan(i, 2), StringComparison.Ordinal);
            var hasLeft = i > 0 && cleaned.Contains(text.AsSpan(i - 1, 2), StringComparison.Ordinal);
            if (!hasRight && !hasLeft)
                return false;
        }
        return true;
    }

    private static bool IsCorrectedAway(string raw, Match run)
    {
        var end = run.Index + run.Length;
        var window = raw.AsSpan(end, Math.Min(MaxCorrectionRun, raw.Length - end));
        foreach (var marker in SpokenCorrections)
            if (window.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="digits"/> was retracted shortly afterwards ("设成 10 秒，不对，改成 30 秒")
    /// or stands as a negated contrast ("设为 30 秒，不是 15 分钟") alongside at least one other active number.
    /// </summary>
    internal static bool IsPrunableSpokenNumber(string raw, Match digits, int nonRetractedDigitCount)
    {
        if (HasRetractionLookahead(raw, digits))
            return true;

        if (nonRetractedDigitCount < 2)
            return false;

        var prefixStart = Math.Max(0, digits.Index - 10);
        var before = raw.AsSpan(prefixStart, digits.Index - prefixStart).TrimEnd();
        foreach (var prefix in ContrastNegationPrefixes)
        {
            if (before.EndsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static bool HasRetractionLookahead(string raw, Match digits)
    {
        var end = digits.Index + digits.Length;
        var window = raw.AsSpan(end, Math.Min(24, raw.Length - end));
        var newline = window.IndexOf('\n');
        if (newline >= 0)
            window = window[..newline];
        foreach (var marker in NumberRetractionMarkers)
        {
            if (window.Contains(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The deleted text is a stutter: one or more repetitions of a motif ("我们" in "我们我们", or "原原" in "原原原")
    /// whose motif stands immediately before or after the deleted span.
    /// </summary>
    private static bool IsRepetition(string raw, int start, int length)
    {
        var text = raw.AsSpan(start, length);
        for (var period = 1; period <= length; period++)
        {
            if (length % period != 0)
                continue;
            var motif = text[..period];
            var periodic = true;
            for (var offset = period; offset < length; offset += period)
            {
                if (!text.Slice(offset, period).SequenceEqual(motif))
                {
                    periodic = false;
                    break;
                }
            }
            if (!periodic)
                continue;

            // Pure single-character acoustic/onomatopoeia loops of 4+ identical characters ("噔噔噔噔噔噔噔噔") may be removed altogether.
            if (period == 1 && length >= 4 && !Negations.Contains(text[0]) && !SpokenDigits.Contains(text[0]))
                return true;

            var before = start >= period && raw.AsSpan(start - period, period).SequenceEqual(motif);
            var after = start + length + period <= raw.Length && raw.AsSpan(start + length, period).SequenceEqual(motif);
            if (before || after)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Counts how many additional glossary term occurrences appeared in <paramref name="cleaned"/> compared with
    /// <paramref name="raw"/>, matching whole ASCII identifiers so "GitHub" never double-counts "Git" and a term
    /// already spoken once earlier in the dictation ("skill") still counts when a second misheard occurrence ("Scale") is repaired.
    /// </summary>
    private static int CountGainedGlossaryTerms(string raw, string cleaned, IReadOnlyList<string> glossary)
    {
        if (glossary.Count == 0)
            return 0;

        var rawScan = CollapseIntraAsciiTermSpaces(raw, glossary);
        var cleanedScan = CollapseIntraAsciiTermSpaces(cleaned, glossary);
        var rawUsed = new bool[rawScan.Length];
        var cleanedUsed = new bool[cleanedScan.Length];
        var gains = 0;

        foreach (var term in glossary.Select(NormalizeTermForBoundaryScan).Where(t => t.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(t => t.Length))
        {
            var inRaw = CountBoundaryMatches(rawScan, term, rawUsed);
            var inCleaned = CountBoundaryMatches(cleanedScan, term, cleanedUsed);
            if (inCleaned > inRaw)
                gains += inCleaned - inRaw;
        }

        return gains;
    }

    private static string CollapseIntraAsciiTermSpaces(string text, IReadOnlyList<string> glossary)
    {
        var result = text;
        foreach (var multiWord in glossary.Where(g => g.Contains(' ')).OrderByDescending(g => g.Length))
            result = Regex.Replace(result, Regex.Escape(multiWord).Replace(@"\ ", @"\s+"), multiWord.Replace(" ", ""), RegexOptions.IgnoreCase);
        return result;
    }

    private static string NormalizeTermForBoundaryScan(string term) =>
        new(term.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int CountBoundaryMatches(string text, string term, bool[] consumed)
    {
        var asciiStart = IsAsciiAlphaNum(term[0]);
        var asciiEnd = IsAsciiAlphaNum(term[^1]);
        var count = 0;
        for (var i = 0; i <= text.Length - term.Length; i++)
        {
            if (consumed[i])
                continue;
            if (asciiStart && i > 0 && IsAsciiAlphaNum(text[i - 1]))
                continue;
            if (asciiEnd && i + term.Length < text.Length && IsAsciiAlphaNum(text[i + term.Length]))
                continue;

            var matched = true;
            for (var k = 0; k < term.Length; k++)
            {
                if (consumed[i + k] || char.ToLowerInvariant(text[i + k]) != term[k])
                {
                    matched = false;
                    break;
                }
            }
            if (!matched)
                continue;

            for (var k = 0; k < term.Length; k++)
                consumed[i + k] = true;
            count++;
            i += term.Length - 1;
        }
        return count;
    }

    private static bool IsAsciiAlphaNum(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');

    /// <summary>
    /// Counts lost English words that were replaced by a phonetically close English word already spoken elsewhere in
    /// the same dictation (for example "skill" spoken in sentence 1, misheard as "Scale" in sentence 7, and repaired to "Skill").
    /// </summary>
    private static int CountInContextEnglishRepairs(string raw, string cleaned, IReadOnlyList<string> lostRuns)
    {
        if (lostRuns.Count == 0)
            return 0;

        var rawCounts = Words(raw).GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var cleanedCounts = Words(cleaned).GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var availableGains = cleanedCounts
            .Where(kv => rawCounts.TryGetValue(kv.Key, out var r) && r > 0 && kv.Value > r)
            .ToDictionary(kv => kv.Key, kv => kv.Value - rawCounts[kv.Key], StringComparer.Ordinal);

        var repaired = 0;
        foreach (var lost in lostRuns)
        {
            var match = availableGains.Keys.FirstOrDefault(candidate => availableGains[candidate] > 0 && ArePhoneticallyCloseAscii(lost, candidate));
            if (match is null)
                continue;
            availableGains[match]--;
            repaired++;
        }
        return repaired;
    }

    private static int CountDirectGlossaryPhoneticRepairs(
        IReadOnlyList<string> lostRuns,
        string cleanedNorm,
        IReadOnlyList<string> effectiveGlossary)
    {
        if (lostRuns.Count == 0 || effectiveGlossary.Count == 0)
            return 0;

        var glossaryWords = effectiveGlossary
            .SelectMany(Words)
            .Where(w => w.Length >= 3 && cleanedNorm.Contains(w, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return lostRuns.Count(lost => glossaryWords.Any(target => ArePhoneticallyCloseAscii(lost, target)));
    }

    internal static bool ArePhoneticallyCloseAscii(string a, string b)
    {
        if (a.Length < 3 || b.Length < 3)
            return false;

        // Code-switched ASR commonly truncates English codas before Chinese particles ("前端 agent 的" -> "前端 age 的").
        if (b.Length > a.Length && b.Length - a.Length <= 3 && b.StartsWith(a, StringComparison.Ordinal))
            return true;
        if (a.Length > b.Length && a.Length - b.Length <= 3 && a.StartsWith(b, StringComparison.Ordinal))
            return true;

        var skelA = ConsonantSkeleton(a);
        var skelB = ConsonantSkeleton(b);
        var coreA = StripLeadingVowel(skelA);
        var coreB = StripLeadingVowel(skelB);
        if (coreA.Length >= 3 && coreB.Length >= 3 && EditDistance(coreA, coreB) <= 1)
            return true;

        if (skelA.Length < 2 || skelB.Length < 2 || skelA[0] != skelB[0])
            return false;
        if (skelA == skelB)
            return Math.Abs(a.Length - b.Length) <= 2;
        return EditDistance(a, b) <= (Math.Min(a.Length, b.Length) >= 5 ? 2 : 1);
    }

    private static string StripLeadingVowel(string skel) =>
        skel.Length > 1 && skel[0] is 'a' or 'e' or 'i' or 'o' or 'u' or 'y' ? skel[1..] : skel;

    private static string ConsonantSkeleton(string word)
    {
        var sb = new StringBuilder(word.Length);
        char prev = '\0';
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i] switch
            {
                'c' or 'q' => 'k',
                _ => word[i]
            };
            var isVowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (i > 0 && isVowel)
                continue;
            if (c == prev)
                continue;
            sb.Append(c);
            prev = c;
        }
        return sb.ToString();
    }

    private static int EditDistance(string a, string b)
    {
        var dp = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) dp[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = dp[0];
            dp[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var temp = dp[j];
                dp[j] = a[i - 1] == b[j - 1] ? prev : 1 + Math.Min(prev, Math.Min(dp[j], dp[j - 1]));
                prev = temp;
            }
        }
        return dp[b.Length];
    }

    /// <summary>
    /// Aligns raw and cleaned by longest common subsequence.
    /// Returns the runs of raw characters absent from the cleaned text, the number of cleaned characters absent from
    /// the raw text, and the added spans in cleaned paired with their anchor index in raw.
    /// </summary>
    private static (List<(int Start, int Length)> Dropped, int Added, List<(int CleanedStart, int Length, int RawAnchor)> AddedRuns) Align(string raw, string cleaned)
    {
        var n = raw.Length;
        var m = cleaned.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = raw[i] == cleaned[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var dropped = new List<(int, int)>();
        var addedRuns = new List<(int, int, int)>();
        var runStart = -1;
        var addStart = -1;
        var addAnchor = 0;
        int iIdx = 0, jIdx = 0;
        while (iIdx < n || jIdx < m)
        {
            if (iIdx < n && jIdx < m && raw[iIdx] == cleaned[jIdx] && lcs[iIdx, jIdx] == lcs[iIdx + 1, jIdx + 1] + 1)
            {
                if (runStart >= 0) dropped.Add((runStart, iIdx - runStart));
                if (addStart >= 0) addedRuns.Add((addStart, jIdx - addStart, addAnchor));
                runStart = -1;
                addStart = -1;
                iIdx++;
                jIdx++;
            }
            else if (jIdx < m && (iIdx == n || lcs[iIdx, jIdx + 1] >= lcs[iIdx + 1, jIdx]))
            {
                if (addStart < 0)
                {
                    addStart = jIdx;
                    addAnchor = iIdx;
                }
                jIdx++;
            }
            else
            {
                if (runStart < 0) runStart = iIdx;
                iIdx++;
            }
        }
        if (runStart >= 0) dropped.Add((runStart, n - runStart));
        if (addStart >= 0) addedRuns.Add((addStart, m - addStart, addAnchor));
        return (dropped, m - lcs[0, 0], addedRuns);
    }

    /// <summary>Lower-cases and drops punctuation and whitespace.</summary>
    public static string Normalize(string text)
    {
        var buffer = new char[text.Length];
        var n = 0;
        foreach (var c in text)
            if (char.IsLetterOrDigit(c)) buffer[n++] = char.ToLowerInvariant(c);
        return new string(buffer, 0, n);
    }

    private static string Cjk(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (c is >= '\u4e00' and <= '\u9fff') sb.Append(c);
        return sb.ToString();
    }

    private static bool IsProtectedSpokenAsciiRun(
        string run,
        IReadOnlyList<string> effectiveGlossary,
        IReadOnlyList<string> glossaryNorm)
    {
        if (Cleanup.FactAnchorGate.IsCodeIdentifier(run))
            return true;

        var trimmed = run.Trim();
        // Uppercase acronyms/initialisms (OK, API, QPS, MVP) or alphanumeric tags (v2, GTA6).
        if (!trimmed.Contains(' ') && trimmed.Length is >= 2 and <= 6 && trimmed.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
            return true;
        if (trimmed.Any(char.IsAsciiDigit))
            return true;

        foreach (var word in Words(run))
        {
            if (glossaryNorm.Contains(word, StringComparer.Ordinal))
                return true;
            if (effectiveGlossary.Any(g => Words(g).Any(gw => ArePhoneticallyCloseAscii(word, gw))))
                return true;
        }
        return false;
    }

    /// <summary>English words of three letters or more, normalized.</summary>
    private static IEnumerable<string> Words(string text) =>
        AsciiWord().Matches(text).Select(m => Normalize(m.Value));

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]{2,}")]
    private static partial Regex AsciiWord();

    /// <summary>A run of adjacent English words, for example "Max Oce".</summary>
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]*(?:[ \t]+[A-Za-z][A-Za-z0-9]*)*")]
    private static partial Regex AsciiRun();

    [GeneratedRegex(@"^\s*#{1,6}\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex InventedMarkdownHeading();

    [GeneratedRegex(@"^\s*-\s+\[[ xX]\]\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex InventedTaskCheckbox();
}
