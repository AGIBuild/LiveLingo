namespace LiveLingo.Core.Translation;

/// <summary>
/// Splits text at natural sentence and paragraph boundaries for incremental translation.
///
/// Segmentation strategy:
///  - Atomic-sentence pre-pass: any text with ≥ 2 sentence-end markers is split per
///    sentence regardless of total length. Local LLMs (Gemma/Qwen) frequently drop the
///    trailing sentence when multiple clauses share a single prompt, so each sentence
///    gets its own translation call and the results are re-joined by the pipeline.
///  - Single-sentence path: short texts are returned verbatim; long ones fall back to
///    paragraph → sentence → word → hard-cut splitting capped by <paramref name="maxCharsPerSegment"/>.
///
/// Content is preserved: reassembling the segments (with break-appropriate separators)
/// reproduces the original text modulo collapsing runs of whitespace.
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

        var trimmed = text.Trim();
        var sentences = SplitIntoSentences(trimmed);

        if (sentences.Count > 1)
            return ExpandSentences(sentences, maxCharsPerSegment);

        if (trimmed.Length <= maxCharsPerSegment)
            return [new TextSegment(trimmed, SegmentBreak.None)];

        return SplitLongText(trimmed, maxCharsPerSegment);
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

    private static List<Sentence> SplitIntoSentences(string text)
    {
        var sentences = new List<Sentence>();
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

            // Advance over inter-sentence whitespace and detect paragraph break (2+ newlines).
            var afterWs = endOfPunct;
            var newlineCount = 0;
            while (afterWs < text.Length)
            {
                var w = text[afterWs];
                if (w == '\n') { newlineCount++; afterWs++; }
                else if (w == ' ' || w == '\t' || w == '\r') afterWs++;
                else break;
            }

            var body = text[start..endOfPunct].Trim();
            if (body.Length > 0)
                sentences.Add(new Sentence(body, FollowedByParagraph: newlineCount >= 2));

            i = afterWs - 1; // compensate for loop ++
            start = afterWs;
        }

        if (start < text.Length)
        {
            var tail = text[start..].Trim();
            if (tail.Length > 0)
                sentences.Add(new Sentence(tail, FollowedByParagraph: false));
        }

        return sentences;
    }

    private static IReadOnlyList<TextSegment> ExpandSentences(
        List<Sentence> sentences, int maxCharsPerSegment)
    {
        var result = new List<TextSegment>(sentences.Count);

        for (var i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            var isLast = i == sentences.Count - 1;
            var breakKind = isLast
                ? SegmentBreak.None
                : (s.FollowedByParagraph ? SegmentBreak.Paragraph : SegmentBreak.Sentence);

            if (s.Text.Length > maxCharsPerSegment)
            {
                var sub = SplitLongText(s.Text, maxCharsPerSegment);
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
                result.Add(new TextSegment(s.Text, breakKind));
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

        // 1. Paragraph break: \n\n or \r\n\r\n – search backwards from end
        for (var i = end; i > start + 1; i--)
        {
            if (i < text.Length && text[i - 1] == '\n')
            {
                var prevIsNewline = i >= 2 && (text[i - 2] == '\n' ||
                    (text[i - 2] == '\r' && i >= 3 && text[i - 3] == '\n'));
                if (prevIsNewline)
                    return (i, SegmentBreak.Paragraph);

                if (i + 1 < text.Length && text[i] == '\n')
                    return (i - 1, SegmentBreak.Paragraph);
            }
        }

        // 2. Sentence-end punctuation:
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

        // 3. Whitespace word boundary
        for (var i = end - 1; i > start; i--)
        {
            if (text[i] == ' ' || text[i] == '\t')
                return (i, SegmentBreak.Word);
        }

        // 4. Hard cut (no natural break found)
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

    private readonly record struct Sentence(string Text, bool FollowedByParagraph);
}

/// <summary>Describes what kind of natural boundary was detected after this segment.</summary>
public enum SegmentBreak
{
    None,
    Word,
    Sentence,
    Paragraph
}

/// <summary>A segment of source text with metadata about the boundary after it.</summary>
public readonly record struct TextSegment(string Text, SegmentBreak BreakAfter);
