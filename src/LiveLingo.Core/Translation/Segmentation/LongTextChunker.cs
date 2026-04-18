namespace LiveLingo.Core.Translation.Segmentation;

/// <summary>
/// Cuts a single long line that has no usable sentence boundaries into
/// chunks of at most <c>maxCharsPerSegment</c>. The break point is chosen
/// in priority order:
///   1. Sentence-end punctuation (CJK or Latin with valid boundary).
///   2. Intra-line whitespace (word boundary).
///   3. Hard cut at the budget.
/// </summary>
internal static class LongTextChunker
{
    public static IReadOnlyList<TextSegment> Split(string trimmed, int maxCharsPerSegment)
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

    public static (int Position, SegmentBreak Kind) FindBreakPoint(
        string text, int start, int maxLen)
    {
        var end = start + maxLen;

        // 1. Sentence-end punctuation.
        for (var i = end - 1; i > start; i--)
        {
            if (SentenceBoundary.IsCjkEnd(text[i]))
                return (i + 1, SegmentBreak.Sentence);

            if (SentenceBoundary.IsLatinEnd(text[i]) &&
                SentenceBoundary.IsLatinBoundary(text, i))
            {
                return (i + 1, SegmentBreak.Sentence);
            }
        }

        // 2. Whitespace word boundary.
        for (var i = end - 1; i > start; i--)
        {
            if (text[i] == ' ' || text[i] == '\t')
                return (i, SegmentBreak.Word);
        }

        // 3. Hard cut.
        return (end, SegmentBreak.None);
    }

    public static int SkipWhitespace(string text, int pos)
    {
        while (pos < text.Length &&
               (text[pos] == ' ' || text[pos] == '\t' ||
                text[pos] == '\n' || text[pos] == '\r'))
        {
            pos++;
        }
        return pos;
    }
}
