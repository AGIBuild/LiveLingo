using LiveLingo.Core.Translation;
using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Tests.Translation.Segmentation;

public class LongTextChunkerTests
{
    [Fact]
    public void Split_TextShorterThanLimit_ReturnsSingleNoneSegment()
    {
        var result = LongTextChunker.Split("short text", 100);

        Assert.Single(result);
        Assert.Equal("short text", result[0].Text);
        Assert.Equal(SegmentBreak.None, result[0].BreakAfter);
    }

    [Fact]
    public void Split_PrefersSentenceBoundary_OverWordBoundary()
    {
        // Length 18 with sentence end at index 5 ("First.") and many spaces after.
        var result = LongTextChunker.Split("First. Second word", 12);

        Assert.True(result.Count >= 2);
        Assert.Equal("First.", result[0].Text);
        Assert.Equal(SegmentBreak.Sentence, result[0].BreakAfter);
    }

    [Fact]
    public void Split_FallsBackToWordBoundary_WhenNoSentenceMark()
    {
        // 25 chars, limit 10, no sentence marks → must split at space.
        var result = LongTextChunker.Split("alpha beta gamma delta omega", 10);

        Assert.True(result.Count >= 2);
        Assert.Equal(SegmentBreak.Word, result[0].BreakAfter);
        Assert.DoesNotContain(' ', result[0].Text); // trimmed
    }

    [Fact]
    public void Split_HardCuts_WhenNoBoundaryAvailable()
    {
        // No spaces, no marks → hard cut at exactly maxLen.
        var input = new string('a', 25);
        var result = LongTextChunker.Split(input, 10);

        Assert.True(result.Count >= 2);
        Assert.Equal(10, result[0].Text.Length);
        Assert.Equal(SegmentBreak.None, result[0].BreakAfter);
    }

    [Fact]
    public void Split_TrailingWhitespaceTail_DoesNotProduceEmptySegment()
    {
        // After the first cut, the tail is "    " which trims to "".
        // The "tail.Length > 0" guard must hold; mutating it to ">= 0"
        // would emit a phantom empty segment.
        var result = LongTextChunker.Split("alpha beta gamma     ", 12);

        Assert.All(result, s => Assert.False(string.IsNullOrEmpty(s.Text)));
    }

    [Fact]
    public void Split_MidStreamWhitespaceChunk_DoesNotProduceEmptySegment()
    {
        // A run of spaces in the middle, sized to land exactly at the limit:
        // first chunk should be "alpha", and the rest should be the trimmed
        // remainder — no empty segment for the consumed whitespace.
        var result = LongTextChunker.Split("alpha               beta", 6);

        Assert.All(result, s => Assert.False(string.IsNullOrEmpty(s.Text)));
        Assert.Contains(result, s => s.Text == "alpha");
        Assert.Contains(result, s => s.Text == "beta");
    }

    [Fact]
    public void Split_DoesNotBreakOnLatinDecimal()
    {
        // "x 3.14 y..." — the period at index 6 is a decimal, not a sentence end.
        var input = "x 3.14 yyyyyyyy zzzzz";
        var result = LongTextChunker.Split(input, 10);

        // First segment must NOT end at "x 3.14"; it should fall to a word break.
        Assert.NotEqual(SegmentBreak.Sentence, result[0].BreakAfter);
    }

    [Fact]
    public void Split_DoesBreakOnCjkSentenceMark()
    {
        var result = LongTextChunker.Split("你好世界。再见朋友们。", 6);

        Assert.True(result.Count >= 2);
        Assert.Equal("你好世界。", result[0].Text);
        Assert.Equal(SegmentBreak.Sentence, result[0].BreakAfter);
    }

    [Fact]
    public void FindBreakPoint_ReturnsHardCut_WhenNoBoundary()
    {
        var input = new string('a', 50);
        var (pos, kind) = LongTextChunker.FindBreakPoint(input, 0, 20);

        Assert.Equal(20, pos);
        Assert.Equal(SegmentBreak.None, kind);
    }

    [Fact]
    public void FindBreakPoint_PicksLatestSentenceMarkBeforeLimit()
    {
        // All-uppercase follow chars so the Latin boundary heuristic accepts each "." as a real boundary.
        var input = "A. B. C. D. E.";
        var (pos, kind) = LongTextChunker.FindBreakPoint(input, 0, 9);

        Assert.Equal(SegmentBreak.Sentence, kind);
        Assert.Equal('.', input[pos - 1]);
        // Latest "." with valid boundary before index 9 is at index 7 ("C."), so pos = 8.
        Assert.Equal(8, pos);
    }

    [Fact]
    public void FindBreakPoint_PicksLatestWhitespace_WhenNoSentenceMark()
    {
        var input = "alpha beta gamma";
        var (pos, kind) = LongTextChunker.FindBreakPoint(input, 0, 12);

        Assert.Equal(SegmentBreak.Word, kind);
        Assert.Equal(' ', input[pos]);
        Assert.Equal(10, pos); // last space before index 12
    }

    [Fact]
    public void FindBreakPoint_RespectsLowercaseFollow_DoesNotSplitAtAbbreviationDot()
    {
        // "mr. smith" — lowercase follow, so the heuristic rejects "." as a sentence end.
        var input = "mr. smith arrived later today okay";
        var (_, kind) = LongTextChunker.FindBreakPoint(input, 0, 14);

        Assert.Equal(SegmentBreak.Word, kind);
    }

    [Fact]
    public void SkipWhitespace_AdvancesPastSpacesTabsAndNewlines()
    {
        // " ", " ", "\t", "\n" — four whitespace chars, then 'a' at index 4.
        Assert.Equal(4, LongTextChunker.SkipWhitespace("  \t\nabc", 0));
    }

    [Fact]
    public void SkipWhitespace_AtNonWhitespace_ReturnsSamePosition()
    {
        Assert.Equal(2, LongTextChunker.SkipWhitespace("abcdef", 2));
    }

    [Fact]
    public void SkipWhitespace_AtEndOfTrailingWhitespace_ReturnsStringLength()
    {
        // "abc   " (length 6): from index 3 we walk three spaces and stop at 6 (EOS).
        Assert.Equal(6, LongTextChunker.SkipWhitespace("abc   ", 3));
    }

    [Fact]
    public void SkipWhitespace_PastStringLength_IsBoundedByLength()
    {
        // Caller never passes a position past Length, but if it does we return it
        // unchanged because the loop guard rejects the index immediately.
        Assert.Equal(6, LongTextChunker.SkipWhitespace("abc   ", 6));
    }
}
