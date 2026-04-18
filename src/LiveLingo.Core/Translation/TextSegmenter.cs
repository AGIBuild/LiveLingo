using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Splits text at natural sentence, line, and paragraph boundaries for incremental translation.
///
/// Acts as the public facade that orchestrates four single-purpose collaborators:
///   <see cref="LineSplitter"/>     — newline normalisation and blank-line folding.
///   <see cref="SentenceSplitter"/> — CJK + Latin sentence boundary detection inside one line.
///   <see cref="LongTextChunker"/>  — sentence/word/hard-cut chunking when a single sentence
///                                    or line exceeds <see cref="DefaultMaxCharsPerSegment"/>.
///   <see cref="UnitPlanner"/>      — grouping line-connected segments into translation units.
///
/// Segmentation strategy:
///  - Line-structure pre-pass: the input is first split on raw newlines so that
///    user-authored line breaks (single \n) and paragraph breaks (blank lines)
///    are preserved as first-class segment boundaries.
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

    public IReadOnlyList<TextSegment> Segment(
        string text, int maxCharsPerSegment = DefaultMaxCharsPerSegment)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = LineSplitter.Split(text);
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
    /// instead of being fragmented per line.
    /// </summary>
    public IReadOnlyList<TranslationUnit> PlanUnits(
        IReadOnlyList<TextSegment> segments,
        int maxCharsPerUnit = DefaultMaxCharsPerSegment)
        => UnitPlanner.Plan(segments, maxCharsPerUnit);

    /// <summary>
    /// True when <paramref name="text"/> ends with a sentence-terminating mark
    /// after trimming trailing whitespace. Latin abbreviation heuristic is
    /// intentionally not applied here — a line ending in "." is treated as a
    /// complete thought for unit-planning purposes.
    /// </summary>
    internal static bool EndsWithSentenceMark(string text)
        => SentenceBoundary.EndsWithMark(text.AsSpan());

    /// <summary>
    /// Counts sentence-ending punctuation that would drive a segmentation split.
    /// Latin marks (.!?) only count when followed by whitespace or end-of-input,
    /// so abbreviations and decimals are not mistaken for sentence boundaries.
    /// </summary>
    public static int CountSentenceEndings(string text)
        => SentenceBoundary.CountEndings(text);

    private static bool IsCjkTarget(string? lang)
    {
        if (string.IsNullOrEmpty(lang)) return false;
        return lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
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

        var sentences = SentenceSplitter.Split(line);
        if (sentences.Count > 1)
            return ExpandSentences(sentences, maxCharsPerSegment);

        if (line.Length <= maxCharsPerSegment)
            return [new TextSegment(line, SegmentBreak.None)];

        return LongTextChunker.Split(line, maxCharsPerSegment);
    }

    private static IReadOnlyList<TextSegment> ExpandSentences(
        IReadOnlyList<string> sentences, int maxCharsPerSegment)
    {
        var result = new List<TextSegment>(sentences.Count);

        for (var i = 0; i < sentences.Count; i++)
        {
            var body = sentences[i];
            var isLast = i == sentences.Count - 1;
            var breakKind = isLast ? SegmentBreak.None : SegmentBreak.Sentence;

            if (body.Length > maxCharsPerSegment)
            {
                var sub = LongTextChunker.Split(body, maxCharsPerSegment);
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
