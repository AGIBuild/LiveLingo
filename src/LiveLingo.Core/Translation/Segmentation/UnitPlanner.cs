using System.Text;

namespace LiveLingo.Core.Translation.Segmentation;

/// <summary>
/// Groups adjacent line-connected segments into <see cref="TranslationUnit"/>s
/// so the engine sees enough context to translate a multi-line thought as one
/// utterance, while still letting the pipeline recover per-segment outputs by
/// splitting the response on '\n'.
///
/// Grouping rules:
///   - Always start a new unit at a paragraph break.
///   - Always start a new unit when adding the next segment would exceed
///     <c>maxCharsPerUnit</c>.
///   - Otherwise consume segments until the previous segment's break is not
///     <see cref="SegmentBreak.Line"/> or the previous segment ends with a
///     sentence-terminating mark.
/// </summary>
internal static class UnitPlanner
{
    public static IReadOnlyList<TranslationUnit> Plan(
        IReadOnlyList<TextSegment> segments,
        int maxCharsPerUnit)
    {
        if (segments is null || segments.Count == 0)
            return [];

        var units = new List<TranslationUnit>(segments.Count);
        var i = 0;

        while (i < segments.Count)
        {
            var startIndex = i;
            var combined = segments[i].Text;
            i++;

            while (i < segments.Count)
            {
                var prev = segments[i - 1];
                if (prev.BreakAfter != SegmentBreak.Line) break;
                if (SentenceBoundary.EndsWithMark(prev.Text)) break;

                var nextLength = combined.Length + 1 + segments[i].Text.Length;
                if (nextLength > maxCharsPerUnit) break;

                combined = $"{combined}\n{segments[i].Text}";
                i++;
            }

            var count = i - startIndex;
            var lastSegment = segments[startIndex + count - 1];
            var sourceText = count == 1
                ? lastSegment.Text
                : BuildSource(segments, startIndex, count);

            units.Add(new TranslationUnit(
                sourceText,
                startIndex,
                count,
                lastSegment.BreakAfter));
        }

        return units;
    }

    private static string BuildSource(
        IReadOnlyList<TextSegment> segments, int start, int count)
    {
        var builder = new StringBuilder(capacity: count * 32);
        for (var k = 0; k < count; k++)
        {
            if (k > 0) builder.Append('\n');
            builder.Append(segments[start + k].Text);
        }
        return builder.ToString();
    }
}
