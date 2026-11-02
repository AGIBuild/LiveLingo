using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LiveLingo.Core.Drafting;

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
        "然后呢", "所以说", "对吧", "那个", "这个", "就是", "的话", "那么", "然后", "其实", "一下",
        "呃", "嗯", "啊", "哦", "额", "呢", "呀", "吧", "嘛"
    ];
    private static readonly string[] CorrectionMarkers = ["不对", "不是", "说错了", "错了", "我是说", "应该是", "改成", "更正"];

    /// <summary>Phrases that only ever announce a self-correction; "不是" or "改成" also occur in ordinary statements.</summary>
    private static readonly string[] SpokenCorrections = ["不对", "说错了", "我是说", "更正"];

    /// <summary>A deleted run counts as restated when this share of its character pairs still occurs in the cleaned text.</summary>
    private const double RestatedCoverage = 0.6;

    private const string Negations = "不没别无非未否";

    public static GateResult Check(string raw, string cleaned, IReadOnlyList<string>? glossary = null)
    {
        if (string.IsNullOrWhiteSpace(cleaned))
            return GateResult.Refuse(GateReason.Empty);

        // One line in, one line out. A line break means the model echoed the prompt layout ("词表：… 目标：…").
        if (cleaned.Contains('\n') && !raw.Contains('\n'))
            return GateResult.Refuse(GateReason.MultiLine);

        foreach (var (open, close) in QuotePairs)
            if (Balance(raw, open, close) != Balance(cleaned, open, close))
                return GateResult.Refuse(GateReason.Brackets);

        var rawNorm = Normalize(raw);
        var cleanedNorm = Normalize(cleaned);
        glossary ??= [];
        var glossaryNorm = glossary.Select(Normalize).Where(g => g.Length > 0).ToList();

        foreach (Match digits in DigitRun().Matches(raw))
            if (!cleanedNorm.Contains(digits.Value, StringComparison.Ordinal))
                return GateResult.Refuse(GateReason.NumberLost, digits.Value);

        // Numbers are never invented: turning garbled Chinese numerals into digits is a guess, so it is left to the recognizer.
        foreach (Match digits in DigitRun().Matches(cleaned))
            if (!rawNorm.Contains(digits.Value, StringComparison.Ordinal))
                return GateResult.Refuse(GateReason.NumberInvented, digits.Value);

        // An English word the cleaner removed is acceptable only when a glossary term took its place.
        // A run of adjacent English words ("Max Oce") is one phonetic guess, so it needs one replacement.
        // A word the speaker corrects right after saying it ("click 事件，不对，是 command 属性") is dropped on purpose.
        var lostRuns = AsciiRun().Matches(raw)
            .Where(run => !IsCorrectedAway(raw, run))
            .Select(run => Words(run.Value).FirstOrDefault(w => !cleanedNorm.Contains(w, StringComparison.Ordinal)))
            .Where(w => w is not null)
            .ToList();
        var replacements = glossaryNorm.Count(g => cleanedNorm.Contains(g, StringComparison.Ordinal) && !rawNorm.Contains(g, StringComparison.Ordinal));
        if (lostRuns.Count > replacements)
            return GateResult.Refuse(GateReason.TermLost, lostRuns[0]!);

        // An English word the cleaner introduced must come from the glossary.
        foreach (var word in Words(cleaned))
            if (!rawNorm.Contains(word, StringComparison.Ordinal) && !glossaryNorm.Any(g => g.Contains(word, StringComparison.Ordinal)))
                return GateResult.Refuse(GateReason.TermNotInGlossary, word);

        var rawCjk = Cjk(raw);
        var cleanedCjk = Cjk(cleaned);
        if (rawCjk.Length == 0) return GateResult.Accept;

        var ratio = (double)cleanedCjk.Length / rawCjk.Length;
        var shown = (ratio * 100).ToString("F0", CultureInfo.InvariantCulture) + "%";
        if (ratio < MinLengthRatio) return GateResult.Refuse(GateReason.TooShort, shown);
        if (ratio > MaxLengthRatio) return GateResult.Refuse(GateReason.TooLong, shown);

        var (dropped, added) = Align(rawCjk, cleanedCjk);
        if (added > Math.Max(6, cleanedCjk.Length / 5))
            return GateResult.Refuse(GateReason.Added, added.ToString(CultureInfo.InvariantCulture));

        var garbageBudget = replacements * GlossaryDropPerReplacement;
        var slack = Math.Max(MinSmallDrops, (int)(rawCjk.Length * SmallDropShare));
        foreach (var (start, length) in dropped)
        {
            if (IsExplainedDeletion(rawCjk, start, length, cleanedCjk)) continue;
            if (length <= garbageBudget)
            {
                garbageBudget -= length;
                continue;
            }
            var small = SmallDrop(rawCjk.Substring(start, length));
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

    /// <summary>The characters of a short deletion that is no filler and holds no negation; 0 when it is not a small drop.</summary>
    private static int SmallDrop(string text)
    {
        var rest = WithoutFillers(text);
        return rest.Length is > 0 and <= MaxSmallDrop && !rest.Any(c => Negations.Contains(c)) ? rest.Length : 0;
    }

    private static readonly (char Open, char Close)[] QuotePairs =
        [('“', '”'), ('‘', '’'), ('「', '」'), ('『', '』'), ('（', '）'), ('(', ')'), ('《', '》')];

    private static int Balance(string text, char open, char close) => text.Count(c => c == open) - text.Count(c => c == close);

    private static bool IsExplainedDeletion(string raw, int start, int length, string cleaned)
    {
        var text = raw.Substring(start, length);
        if (length <= 1) return !Negations.Contains(text[0]);

        var rest = WithoutFillers(text);
        if (rest.Length <= 1 && !rest.Any(c => Negations.Contains(c))) return true;
        if (IsRepetition(raw, start, length)) return true;
        if (length <= MaxCorrectionRun && CorrectionMarkers.Any(m => text.Contains(m, StringComparison.Ordinal))) return true;
        return IsRestated(rest, cleaned);
    }

    private static string WithoutFillers(string text)
    {
        foreach (var filler in Fillers) text = text.Replace(filler, "", StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// The deleted text says again what the cleaned text still says ("准确率很高。准确率不是很高"), so nothing is lost.
    /// A deleted negation is never excused: dropping "不" turns a statement into its opposite.
    /// </summary>
    private static bool IsRestated(string text, string cleaned)
    {
        if (text.Any(c => Negations.Contains(c))) return false;

        var pairs = text.Length - 1;
        var covered = 0;
        for (var i = 0; i < pairs; i++)
            if (cleaned.Contains(text.AsSpan(i, 2), StringComparison.Ordinal))
                covered++;
        return covered >= pairs * RestatedCoverage;
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

    /// <summary>The deleted text is a stutter: the same characters stand right before or right after it.</summary>
    private static bool IsRepetition(string raw, int start, int length)
    {
        var text = raw.Substring(start, length);
        var before = start >= length && raw.AsSpan(start - length, length).SequenceEqual(text);
        var after = start + 2 * length <= raw.Length && raw.AsSpan(start + length, length).SequenceEqual(text);
        return before || after;
    }

    /// <summary>
    /// Aligns raw and cleaned by longest common subsequence.
    /// Returns the runs of raw characters absent from the cleaned text and the number of cleaned characters absent from the raw text.
    /// </summary>
    private static (List<(int Start, int Length)> Dropped, int Added) Align(string raw, string cleaned)
    {
        var n = raw.Length;
        var m = cleaned.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = raw[i] == cleaned[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var dropped = new List<(int, int)>();
        var runStart = -1;
        for (int i = 0, j = 0; i < n; )
        {
            if (j < m && raw[i] == cleaned[j] && lcs[i, j] == lcs[i + 1, j + 1] + 1)
            {
                if (runStart >= 0) dropped.Add((runStart, i - runStart));
                runStart = -1;
                i++;
                j++;
            }
            else if (j < m && lcs[i, j + 1] >= lcs[i + 1, j])
            {
                j++;
            }
            else
            {
                if (runStart < 0) runStart = i;
                i++;
            }
        }
        if (runStart >= 0) dropped.Add((runStart, n - runStart));
        return (dropped, m - lcs[0, 0]);
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
}
