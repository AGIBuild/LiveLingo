using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class TextSegmenterTests
{
    private readonly TextSegmenter _segmenter = new();

    // --- Short text ---

    [Fact]
    public void Segment_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(_segmenter.Segment(string.Empty));
        Assert.Empty(_segmenter.Segment("   "));
    }

    [Fact]
    public void Segment_TextAtLimit_ReturnsSingleSegment()
    {
        var text = new string('a', TextSegmenter.DefaultMaxCharsPerSegment);
        var result = _segmenter.Segment(text);

        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public void Segment_ShortText_ReturnsSingleSegment()
    {
        const string text = "Hello world.";
        var result = _segmenter.Segment(text);

        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
        Assert.Equal(SegmentBreak.None, result[0].BreakAfter);
    }

    // --- Paragraph splitting ---

    [Fact]
    public void Segment_LongTextWithParagraphBreak_SplitsAtDoubleNewline()
    {
        var para1 = new string('a', 300);
        var para2 = new string('b', 300);
        var text = $"{para1}\n\n{para2}";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(para1, result[0].Text);
        Assert.Equal(SegmentBreak.Paragraph, result[0].BreakAfter);
        Assert.Equal(para2, result[1].Text);
    }

    [Fact]
    public void Segment_MultipleParagraphs_AllPreserved()
    {
        var para1 = new string('a', 300);
        var para2 = new string('b', 300);
        var para3 = new string('c', 300);
        var text = $"{para1}\n\n{para2}\n\n{para3}";

        var result = _segmenter.Segment(text);

        Assert.Equal(3, result.Count);
        Assert.Equal(para1, result[0].Text);
        Assert.Equal(para2, result[1].Text);
        Assert.Equal(para3, result[2].Text);
    }

    // --- Sentence splitting ---

    [Fact]
    public void Segment_LongTextWithSentences_SplitsAtSentenceEnd()
    {
        // Construct text with clear sentence boundary within the limit
        var prefix = new string('a', 200);
        var suffix = new string('b', 400);
        var text = $"{prefix}. {suffix}";

        var result = _segmenter.Segment(text);

        Assert.True(result.Count >= 2);
        Assert.True(result[0].Text.EndsWith('.'));
    }

    [Fact]
    public void Segment_ChineseSentences_SplitsAtChinesePunctuation()
    {
        var part1 = new string('你', 200);
        var part2 = new string('好', 400);
        var text = $"{part1}。{part2}";

        var result = _segmenter.Segment(text);

        Assert.True(result.Count >= 2);
        Assert.True(result[0].Text.EndsWith('。'));
    }

    [Fact]
    public void Segment_SentenceBoundary_BreakTypeIsSentence()
    {
        var part1 = new string('a', 200);
        var part2 = new string('b', 400);
        var text = $"{part1}. {part2}";

        var result = _segmenter.Segment(text);

        // All segments except the last should have a detected break type
        foreach (var seg in result.Take(result.Count - 1))
        {
            Assert.True(seg.BreakAfter is SegmentBreak.Sentence or SegmentBreak.Word or SegmentBreak.Paragraph);
        }
    }

    // --- Word-boundary splitting ---

    [Fact]
    public void Segment_LongTextNoSentenceBreaks_SplitsAtWordBoundary()
    {
        // 600 chars of words separated by spaces, no sentence ends
        var words = Enumerable.Repeat("word", 150);
        var text = string.Join(" ", words); // 150 * 5 = 750 chars

        var result = _segmenter.Segment(text);

        Assert.True(result.Count >= 2);
        // No segment should contain a mid-word split
        Assert.All(result, s => Assert.DoesNotContain("wordword", s.Text));
    }

    // --- Hard cut ---

    [Fact]
    public void Segment_SingleWordLongerThanLimit_HardCuts()
    {
        var longWord = new string('x', 600); // single word, no spaces
        var maxChars = 200;

        var result = _segmenter.Segment(longWord, maxChars);

        Assert.True(result.Count >= 2);
        Assert.All(result, s => Assert.True(s.Text.Length <= maxChars + 1));
    }

    // --- Content preservation ---

    [Fact]
    public void Segment_AllContentPreserved_NothingDropped()
    {
        var original = string.Join(" ", Enumerable.Repeat("hello world", 100)); // ~1200 chars

        var result = _segmenter.Segment(original);

        var reconstructed = string.Join(" ", result.Select(s => s.Text));
        Assert.Equal(
            original.Trim().Replace("  ", " "),
            reconstructed.Replace("  ", " "));
    }

    [Fact]
    public void Segment_PreservesParagraphContent()
    {
        var para1 = "First paragraph with enough content.";
        var para2 = "Second paragraph that is very long: " + new string('a', 300);
        var para3 = "Third paragraph with final thoughts.";
        var text = $"{para1}\n\n{para2}\n\n{para3}";

        var result = _segmenter.Segment(text, maxCharsPerSegment: 200);

        var allText = string.Join("", result.Select(s => s.Text));
        Assert.Contains("First paragraph", allText);
        Assert.Contains("Second paragraph", allText);
        Assert.Contains("Third paragraph", allText);
    }

    // --- Custom limit ---

    [Fact]
    public void Segment_WithCustomLimit_RespectsLimit()
    {
        var text = new string('a', 250);
        var result = _segmenter.Segment(text, maxCharsPerSegment: 100);

        Assert.True(result.Count >= 2);
        Assert.All(result, s => Assert.True(s.Text.Length <= 100 + 1));
    }

    // --- Single segment for text just at limit ---

    [Fact]
    public void Segment_TextOneBelowLimit_ReturnsSingleSegment()
    {
        var text = new string('a', TextSegmenter.DefaultMaxCharsPerSegment - 1);
        var result = _segmenter.Segment(text);

        Assert.Single(result);
    }

    [Fact]
    public void Segment_TextOneAboveLimit_ReturnsTwoSegments()
    {
        var words = "word " + new string('a', TextSegmenter.DefaultMaxCharsPerSegment);
        var result = _segmenter.Segment(words);

        Assert.True(result.Count >= 2);
    }

    // --- Atomic-sentence pre-pass (regression: multi-sentence short text) ---

    [Fact]
    public void Segment_ShortChineseMultiSentence_SplitsPerSentence()
    {
        // Regression: "你好啊，胆小鬼。 你是不是不知道我是谁？" used to be a single
        // segment, letting the LLM drop the second clause. Atomic-sentence
        // pre-pass now splits per sentence regardless of total length.
        var text = "你好啊，胆小鬼。 你是不是不知道我是谁？";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal("你好啊，胆小鬼。", result[0].Text);
        Assert.Equal(SegmentBreak.Sentence, result[0].BreakAfter);
        Assert.Equal("你是不是不知道我是谁？", result[1].Text);
        Assert.Equal(SegmentBreak.None, result[1].BreakAfter);
    }

    [Fact]
    public void Segment_ShortEnglishMultiSentence_SplitsPerSentence()
    {
        var text = "Hello world. How are you?";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal("Hello world.", result[0].Text);
        Assert.Equal("How are you?", result[1].Text);
    }

    [Fact]
    public void Segment_DoesNotSplitOnAbbreviationDot()
    {
        // "U.S.A" and "3.14" must not count as sentence ends (no ws after dot).
        var text = "The U.S.A. is big. Pi equals 3.14 exactly.";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal("The U.S.A. is big.", result[0].Text);
        Assert.Equal("Pi equals 3.14 exactly.", result[1].Text);
    }

    [Fact]
    public void Segment_ConsecutiveEndPunctuation_TreatedAsSingleBoundary()
    {
        // "?!" or "..." should still be one boundary.
        var text = "Really?! Yes, really... Thanks.";

        var result = _segmenter.Segment(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("Really?!", result[0].Text);
        Assert.Equal("Yes, really...", result[1].Text);
        Assert.Equal("Thanks.", result[2].Text);
    }

    [Fact]
    public void Segment_ParagraphBetweenSentences_MarksParagraphBreak()
    {
        var text = "First sentence.\n\nSecond sentence.";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(SegmentBreak.Paragraph, result[0].BreakAfter);
    }

    [Fact]
    public void Segment_SingleSentence_PreservesLegacyBehaviour()
    {
        // Single sentence with a trailing "." must still return one segment.
        var text = "Hello world.";
        var result = _segmenter.Segment(text);

        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
    }

    [Fact]
    public void Segment_LongSingleSentenceWithAbbreviation_DoesNotBreakInsideAbbreviation()
    {
        // Single logical sentence (one terminal '.') longer than the max-per-segment
        // budget so the long-text path must split. "U.S.A." sits inside the window —
        // the old FindBreakPoint picked abbreviation dots as boundaries; the fix
        // aligns it with IsLatinSentenceBoundary so the split falls on a word gap.
        const string prefix = "Today the entire U.S.A. economy";
        var filler = string.Join(' ', Enumerable.Repeat("alpha", 150));
        var text = $"{prefix} kept expanding while {filler} stopped growing.";

        Assert.True(text.Length > TextSegmenter.DefaultMaxCharsPerSegment,
            "Test precondition: text must exceed max-per-segment budget.");
        Assert.Equal(1, TextSegmenter.CountSentenceEndings(text));

        var result = _segmenter.Segment(text);

        Assert.True(result.Count >= 2, "Expected the long sentence to be split.");
        Assert.DoesNotContain(result, s => s.Text.EndsWith("U.S.A.", StringComparison.Ordinal));
    }

    // --- Single-newline line preservation ---

    [Fact]
    public void Segment_SingleNewlineBetweenLines_MarksLineBreak()
    {
        // Two short lines with no sentence punctuation: the old segmenter
        // coalesced them into a single Sentence-break and lost the hard
        // wrap; the line pre-pass must now surface SegmentBreak.Line.
        var text = "line one\nline two";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal("line one", result[0].Text);
        Assert.Equal(SegmentBreak.Line, result[0].BreakAfter);
        Assert.Equal("line two", result[1].Text);
        Assert.Equal(SegmentBreak.None, result[1].BreakAfter);
    }

    [Fact]
    public void Segment_LineBreakSeparator_IsNewline()
    {
        Assert.Equal("\n", TextSegmenter.JoinSeparatorFor(SegmentBreak.Line, "en"));
        Assert.Equal("\n", TextSegmenter.JoinSeparatorFor(SegmentBreak.Line, "zh"));
        Assert.Equal("\n", TextSegmenter.JoinSeparatorFor(SegmentBreak.Line, null));
    }

    [Fact]
    public void Segment_ThreeLinesWithBlankLineInMiddle_ProducesLineThenParagraph()
    {
        // "first\nsecond\n\nthird" – second/third are separated by a blank
        // line which promotes the boundary to Paragraph while the earlier
        // single newline stays a Line break.
        var text = "first\nsecond\n\nthird";

        var result = _segmenter.Segment(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("first", result[0].Text);
        Assert.Equal(SegmentBreak.Line, result[0].BreakAfter);
        Assert.Equal("second", result[1].Text);
        Assert.Equal(SegmentBreak.Paragraph, result[1].BreakAfter);
        Assert.Equal("third", result[2].Text);
        Assert.Equal(SegmentBreak.None, result[2].BreakAfter);
    }

    [Fact]
    public void Segment_CrlfIsNormalized()
    {
        var text = "alpha\r\nbeta";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(SegmentBreak.Line, result[0].BreakAfter);
    }

    [Fact]
    public void Segment_MultipleSentencesOnASingleLine_StayIntraLine()
    {
        // Sentence splits inside a line must still report Sentence breaks,
        // not Line breaks – we only promote to Line at a real '\n'.
        var text = "Hi there. Bye now.";

        var result = _segmenter.Segment(text);

        Assert.Equal(2, result.Count);
        Assert.Equal(SegmentBreak.Sentence, result[0].BreakAfter);
    }

    [Fact]
    public void Segment_SentencesAcrossTwoLines_LastIntraLineIsLineBreak()
    {
        // "A. B.\nC." – intra-line boundary between A/B is Sentence, line
        // boundary between B and C is Line.
        var text = "A. B.\nC.";

        var result = _segmenter.Segment(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("A.", result[0].Text);
        Assert.Equal(SegmentBreak.Sentence, result[0].BreakAfter);
        Assert.Equal("B.", result[1].Text);
        Assert.Equal(SegmentBreak.Line, result[1].BreakAfter);
        Assert.Equal("C.", result[2].Text);
    }

    // --- CountSentenceEndings (used by TranslationQualityGuard) ---

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello world", 0)]
    [InlineData("Hello world.", 1)]
    [InlineData("Hi. Bye.", 2)]
    [InlineData("你好。再见。", 2)]
    [InlineData("你好啊，胆小鬼。 你是不是不知道我是谁？", 2)]
    [InlineData("The U.S.A. is big.", 1)]          // abbreviations don't count
    [InlineData("Pi is 3.14.", 1)]                  // decimals don't count
    [InlineData("Really?! Yes...", 2)]              // consecutive marks = 1 each boundary
    public void CountSentenceEndings_ReturnsExpected(string text, int expected)
    {
        Assert.Equal(expected, TextSegmenter.CountSentenceEndings(text));
    }
}
