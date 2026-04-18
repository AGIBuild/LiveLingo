using System.Text;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Splits text at natural sentence, line, and paragraph boundaries for incremental translation.
///
/// Segmentation strategy:
///  - Line-structure pre-pass: the input is first split on raw newlines so that
///    user-authored line breaks (single \n) and paragraph breaks (blank lines)
///    are preserved as first-class segment boundaries. Each non-empty line then
///    runs through the atomic-sentence + long-text pipeline below.
///  - Atomic-sentence pre-pass: any line with ≥ 2 sentence-end markers is split per
///    sentence regardless of total length. Local LLMs (Gemma/Qwen) frequently drop the
///    trailing sentence when multiple clauses share a single prompt, so each sentence
///    gets its own translation call and the results are re-joined by the pipeline.
///  - Single-sentence path: short texts are returned verbatim; long ones fall back to
///    paragraph → sentence → word → hard-cut splitting capped by <paramref name="maxCharsPerSegment"/>.
///
/// Content is preserved: reassembling the segments with break-appropriate
/// separators (see <see cref="JoinSeparatorFor"/>) reproduces the original
/// newline layout modulo collapsing runs of intra-line whitespace.
/// </summary>
public sealed class TextSegmenter
{
    /// <summary>Maximum characters per segment. Chosen to stay below the 600-char cloud-routing threshold.</summary>
    public const int DefaultMaxCharsPerSegment = 500;

    // CJK end-marks don't require trailing whitespace; Latin marks do (avoid "3.14", "U.S.A").
    private static readonly char[] CjkSentenceEndChars = ['。', '！', '？', '…'];
    private static readonly char[] LatinSentenceEndChars = ['.', '!', '?'];

    public IReadOnlyList<TextSegment> Segment(
        string text, int maxCharsPerSegment = DefaultMaxCharsPerSegment)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = SplitIntoLines(text);
        if (lines.Count == 0) return [];

        var result = new List<TextSegment>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineSegments = SegmentSingleLine(line.Text, maxCharsPerSegment);
            if (lineSegments.Count == 0) continue;

            var isLastLine = lineIndex == lines.Count - 1;
            var trailingBreak = isLastLine
                ? SegmentBreak.None
                : (line.FollowingBlankLines >= 1 ? SegmentBreak.Paragraph : SegmentBreak.Line);

            for (var i = 0; i < lineSegments.Count; i++)
            {
                var seg = lineSegments[i];
                var isLastInLine = i == lineSegments.Count - 1;
                // Intra-line sentence/word breaks stay as the inner splitter
                // reported them. Only the line's final segment carries the
                // line-level Line/Paragraph break.
                var effectiveBreak = isLastInLine ? trailingBreak : seg.BreakAfter;
                result.Add(new TextSegment(seg.Text, effectiveBreak));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the separator a re-assembler should insert between a segment
    /// and its successor given the break kind and the translation target.
    ///   <see cref="SegmentBreak.Paragraph"/> → "\n\n" (always, to preserve layout).
    ///   <see cref="SegmentBreak.Line"/>      → "\n"  (single hard wrap from the source).
    ///   Sentence / Word / None              → " " for non-CJK targets, "" for CJK targets
    ///                                         (CJK punctuation already carries the gap).
    /// </summary>
    public static string JoinSeparatorFor(SegmentBreak breakAfter, string? targetLanguage)
    {
        return breakAfter switch
        {
            SegmentBreak.Paragraph => "\n\n",
            SegmentBreak.Line => "\n",
            _ => IsCjkTarget(targetLanguage) ? string.Empty : " "
        };
    }

    /// <summary>
    /// Groups adjacent segments connected by a <see cref="SegmentBreak.Line"/>
    /// into a single translation unit so multi-line semantic content (poems,
    /// wrapped sentences, bullet lists) is translated as one cohesive prompt
    /// instead of being fragmented per line. Segments separated by
    /// <see cref="SegmentBreak.Sentence"/> / <see cref="SegmentBreak.Paragraph"/>
    /// / <see cref="SegmentBreak.None"/> remain independent units — this is
    /// the existing atomic-sentence guarantee that prevents local LLMs from
    /// dropping trailing clauses.
    ///
    /// A line that already ends with a sentence-end mark (.!?。！？…) is
    /// considered a complete thought and is NOT merged with the next line,
    /// because there the line break is usually layout/style rather than a
    /// continuation of the same clause.
    /// </summary>
    public IReadOnlyList<TranslationUnit> PlanUnits(
        IReadOnlyList<TextSegment> segments,
        int maxCharsPerUnit = DefaultMaxCharsPerSegment)
    {
        if (segments.Count == 0) return [];

        var units = new List<TranslationUnit>();
        var unitStart = 0;
        var unitLength = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            // 1 char budget is reserved for the embedded '\n' that joins this
            // fragment to the previous one inside the same unit.
            unitLength = i == unitStart
                ? seg.Text.Length
                : unitLength + 1 + seg.Text.Length;

            var isLast = i == segments.Count - 1;
            var canExtend =
                !isLast &&
                seg.BreakAfter == SegmentBreak.Line &&
                !EndsWithSentenceMark(seg.Text) &&
                unitLength + 1 + segments[i + 1].Text.Length <= maxCharsPerUnit;

            if (!canExtend)
            {
                units.Add(new TranslationUnit(
                    BuildUnitSource(segments, unitStart, i),
                    unitStart,
                    i - unitStart + 1,
                    seg.BreakAfter));
                unitStart = i + 1;
                unitLength = 0;
            }
        }

        return units;
    }

    private static string BuildUnitSource(
        IReadOnlyList<TextSegment> segments, int start, int end)
    {
        if (start == end) return segments[start].Text;

        var sb = new StringBuilder();
        for (var i = start; i <= end; i++)
        {
            if (i > start) sb.Append('\n');
            sb.Append(segments[i].Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when <paramref name="text"/> ends with a sentence-terminating mark
    /// (after trimming trailing whitespace). Intentionally does not apply the
    /// Latin abbreviation heuristic – a line that ends in "." is treated as a
    /// complete thought for unit-planning purposes even if it is actually an
    /// abbreviation like "Dr." (rare enough to accept as the trade-off).
    /// </summary>
    internal static bool EndsWithSentenceMark(string text)
    {
        var span = text.AsSpan().TrimEnd();
        if (span.IsEmpty) return false;
        var last = span[^1];
        return Array.IndexOf(CjkSentenceEndChars, last) >= 0 ||
               Array.IndexOf(LatinSentenceEndChars, last) >= 0;
    }

    private static bool IsCjkTarget(string? lang)
    {
        if (string.IsNullOrEmpty(lang)) return false;
        return lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts sentence-ending punctuation that would drive a segmentation split.
    /// Latin marks (.!?) only count when followed by whitespace or end-of-input,
    /// so abbreviations and decimals are not mistaken for sentence boundaries.
    /// </summary>
    public static int CountSentenceEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (Array.IndexOf(CjkSentenceEndChars, c) >= 0)
            {
                count++;
                while (i + 1 < text.Length && IsAnySentenceEnd(text[i + 1])) i++;
                continue;
            }

            if (Array.IndexOf(LatinSentenceEndChars, c) >= 0 &&
                IsLatinSentenceBoundary(text, i))
            {
                count++;
                while (i + 1 < text.Length && IsAnySentenceEnd(text[i + 1])) i++;
            }
        }
        return count;
    }

    /// <summary>
    /// Returns true when the Latin sentence-end mark at <paramref name="position"/>
    /// is followed by a true sentence boundary: whitespace/EOF, and the next
    /// non-whitespace character is not a lowercase letter (which would suggest
    /// the mark closes an abbreviation such as "U.S.A. is big").
    /// </summary>
    private static bool IsLatinSentenceBoundary(string text, int position)
    {
        var next = position + 1;
        if (next >= text.Length) return true;

        var c = text[next];
        if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
            return false;

        while (next < text.Length &&
               (text[next] == ' ' || text[next] == '\t' ||
                text[next] == '\n' || text[next] == '\r'))
            next++;

        if (next >= text.Length) return true;

        // Abbreviation-in-sentence heuristic: "Mr. smith" (lowercase follow)
        // or "U.S.A. is big" must not be treated as a sentence boundary.
        return !char.IsLower(text[next]);
    }

    /// <summary>
    /// Splits the raw input at any newline sequence, collapsing runs of
    /// blank lines into a <see cref="Line.FollowingBlankLines"/> count on
    /// the preceding non-blank line. Pure whitespace lines are treated as
    /// blank. The returned list is strictly non-empty entries.
    /// </summary>
    private static List<Line> SplitIntoLines(string text)
    {
        // Normalize CRLF and bare CR so downstream logic only deals with '\n'.
        // This keeps the segmenter oblivious to the host platform's line endings.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var raw = normalized.Split('\n');

        var result = new List<Line>();
        var i = 0;
        while (i < raw.Length)
        {
            var content = raw[i].Trim();
            if (content.Length == 0) { i++; continue; }

            var blanks = 0;
            var j = i + 1;
            while (j < raw.Length && raw[j].Trim().Length == 0)
            {
                blanks++;
                j++;
            }
            result.Add(new Line(content, blanks));
            i = j;
        }

        return result;
    }

    /// <summary>
    /// Segments a single already-trimmed source line into sentence-level or
    /// long-text chunks. The line-level break (Line/Paragraph) is stamped by
    /// the caller; this routine only emits intra-line Sentence/Word/None.
    /// </summary>
    private static IReadOnlyList<TextSegment> SegmentSingleLine(
        string line, int maxCharsPerSegment)
    {
        if (line.Length == 0) return [];

        var sentences = SplitIntoSentences(line);
        if (sentences.Count > 1)
            return ExpandSentences(sentences, maxCharsPerSegment);

        if (line.Length <= maxCharsPerSegment)
            return [new TextSegment(line, SegmentBreak.None)];

        return SplitLongText(line, maxCharsPerSegment);
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isCjkEnd = Array.IndexOf(CjkSentenceEndChars, c) >= 0;
            var isLatinEnd = Array.IndexOf(LatinSentenceEndChars, c) >= 0;
            if (!isCjkEnd && !isLatinEnd) continue;

            if (isLatinEnd && !IsLatinSentenceBoundary(text, i))
                continue;

            // Absorb consecutive sentence-end punctuation (e.g. "?!", "...")
            var endOfPunct = i + 1;
            while (endOfPunct < text.Length && IsAnySentenceEnd(text[endOfPunct]))
                endOfPunct++;

            // Skip inter-sentence whitespace. Newlines no longer reach this
            // code path – the line pre-pass has already peeled them off.
            var afterWs = endOfPunct;
            while (afterWs < text.Length &&
                   (text[afterWs] == ' ' || text[afterWs] == '\t'))
                afterWs++;

            var body = text[start..endOfPunct].Trim();
            if (body.Length > 0)
                sentences.Add(body);

            i = afterWs - 1; // compensate for loop ++
            start = afterWs;
        }

        if (start < text.Length)
        {
            var tail = text[start..].Trim();
            if (tail.Length > 0)
                sentences.Add(tail);
        }

        return sentences;
    }

    private static IReadOnlyList<TextSegment> ExpandSentences(
        List<string> sentences, int maxCharsPerSegment)
    {
        var result = new List<TextSegment>(sentences.Count);

        for (var i = 0; i < sentences.Count; i++)
        {
            var body = sentences[i];
            var isLast = i == sentences.Count - 1;
            var breakKind = isLast ? SegmentBreak.None : SegmentBreak.Sentence;

            if (body.Length > maxCharsPerSegment)
            {
                var sub = SplitLongText(body, maxCharsPerSegment);
                for (var j = 0; j < sub.Count; j++)
                {
                    var lastOfSub = j == sub.Count - 1;
                    result.Add(lastOfSub
                        ? new TextSegment(sub[j].Text, breakKind)
                        : sub[j]);
                }
            }
            else
            {
                result.Add(new TextSegment(body, breakKind));
            }
        }

        return result;
    }

    private static IReadOnlyList<TextSegment> SplitLongText(string trimmed, int maxCharsPerSegment)
    {
        var segments = new List<TextSegment>();
        var pos = 0;

        while (pos < trimmed.Length)
        {
            var remaining = trimmed.Length - pos;
            if (remaining <= maxCharsPerSegment)
            {
                var tail = trimmed[pos..].Trim();
                if (tail.Length > 0)
                    segments.Add(new TextSegment(tail, SegmentBreak.None));
                break;
            }

            var (breakPos, breakType) = FindBreakPoint(trimmed, pos, maxCharsPerSegment);

            var chunk = trimmed[pos..breakPos].Trim();
            if (chunk.Length > 0)
                segments.Add(new TextSegment(chunk, breakType));

            pos = SkipWhitespace(trimmed, breakPos);
        }

        return segments;
    }

    private static (int pos, SegmentBreak kind) FindBreakPoint(
        string text, int start, int maxLen)
    {
        var end = start + maxLen;

        // The line pre-pass consumes paragraph breaks before this code runs,
        // so FindBreakPoint only needs to think about sentence / word / hard
        // cuts within a single logical line.

        // 1. Sentence-end punctuation:
        //    - CJK marks (。！？…) always split — no trailing space in CJK text.
        //    - Latin marks (.!?) use the same IsLatinSentenceBoundary heuristic as the
        //      atomic-sentence pass, so abbreviations (U.S.A) and decimals (3.14) are
        //      never picked as a break point and both code paths stay consistent.
        for (var i = end - 1; i > start; i--)
        {
            if (Array.IndexOf(CjkSentenceEndChars, text[i]) >= 0)
                return (i + 1, SegmentBreak.Sentence);

            if (Array.IndexOf(LatinSentenceEndChars, text[i]) >= 0 &&
                IsLatinSentenceBoundary(text, i))
            {
                return (i + 1, SegmentBreak.Sentence);
            }
        }

        // 2. Whitespace word boundary
        for (var i = end - 1; i > start; i--)
        {
            if (text[i] == ' ' || text[i] == '\t')
                return (i, SegmentBreak.Word);
        }

        // 3. Hard cut (no natural break found)
        return (end, SegmentBreak.None);
    }

    private static int SkipWhitespace(string text, int pos)
    {
        while (pos < text.Length &&
               (text[pos] == ' ' || text[pos] == '\t' ||
                text[pos] == '\n' || text[pos] == '\r'))
        {
            pos++;
        }
        return pos;
    }

    private static bool IsAnySentenceEnd(char c) =>
        Array.IndexOf(CjkSentenceEndChars, c) >= 0 ||
        Array.IndexOf(LatinSentenceEndChars, c) >= 0;

    private readonly record struct Line(string Text, int FollowingBlankLines);
}

/// <summary>Describes what kind of natural boundary was detected after this segment.</summary>
public enum SegmentBreak
{
    None,
    Word,
    Sentence,
    /// <summary>
    /// A single user-authored newline separates this segment from the next.
    /// Reassemblers should insert "\n" here to preserve the source line layout.
    /// </summary>
    Line,
    Paragraph
}

/// <summary>A segment of source text with metadata about the boundary after it.</summary>
public readonly record struct TextSegment(string Text, SegmentBreak BreakAfter);

/// <summary>
/// One translation call's worth of source. A unit covers one or more adjacent
/// <see cref="TextSegment"/>s that were joined by <see cref="SegmentBreak.Line"/>
/// so they can be translated together while still letting the pipeline recover
/// per-segment outputs by splitting the model's response on newline.
/// </summary>
/// <param name="SourceText">Text to send to the translation engine. Internal
/// fragments are joined with a literal '\n' so the engine can preserve layout.</param>
/// <param name="FirstSegmentIndex">Index into the segment list of the first
/// segment covered by this unit.</param>
/// <param name="SegmentCount">Number of segments covered by this unit (≥ 1).</param>
/// <param name="BreakAfter">The break kind following the unit's last fragment –
/// used by the reassembler to pick the separator between adjacent units.</param>
public readonly record struct TranslationUnit(
    string SourceText,
    int FirstSegmentIndex,
    int SegmentCount,
    SegmentBreak BreakAfter);
