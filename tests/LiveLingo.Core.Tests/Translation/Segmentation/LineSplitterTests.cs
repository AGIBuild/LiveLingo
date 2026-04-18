using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Tests.Translation.Segmentation;

public class LineSplitterTests
{
    [Fact]
    public void Split_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(LineSplitter.Split(string.Empty));
    }

    [Fact]
    public void Split_OnlyWhitespaceLines_ReturnsEmpty()
    {
        Assert.Empty(LineSplitter.Split("   \n\t\n  "));
    }

    [Fact]
    public void Split_SingleLine_ReturnsOneEntryWithZeroBlanks()
    {
        var lines = LineSplitter.Split("Hello world");

        Assert.Single(lines);
        Assert.Equal("Hello world", lines[0].Text);
        Assert.Equal(0, lines[0].FollowingBlankLines);
    }

    [Fact]
    public void Split_TrimsLeadingAndTrailingWhitespace()
    {
        var lines = LineSplitter.Split("  hi  ");

        Assert.Single(lines);
        Assert.Equal("hi", lines[0].Text);
    }

    [Fact]
    public void Split_NormalizesCrlfToLf()
    {
        var lines = LineSplitter.Split("a\r\nb");

        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Text);
        Assert.Equal("b", lines[1].Text);
    }

    [Fact]
    public void Split_NormalizesBareCrToLf()
    {
        var lines = LineSplitter.Split("a\rb");

        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Text);
        Assert.Equal("b", lines[1].Text);
    }

    [Fact]
    public void Split_TwoLinesNoBlankBetween_RecordsZeroFollowingBlanks()
    {
        var lines = LineSplitter.Split("first\nsecond");

        Assert.Equal(2, lines.Count);
        Assert.Equal(0, lines[0].FollowingBlankLines);
        Assert.Equal(0, lines[1].FollowingBlankLines);
    }

    [Fact]
    public void Split_OneBlankLineBetween_RecordsOneFollowingBlank()
    {
        var lines = LineSplitter.Split("first\n\nsecond");

        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].FollowingBlankLines);
        Assert.Equal(0, lines[1].FollowingBlankLines);
    }

    [Fact]
    public void Split_MultipleBlankLines_AllFoldedIntoFollowingCount()
    {
        var lines = LineSplitter.Split("first\n\n\n\nsecond");

        Assert.Equal(2, lines.Count);
        Assert.Equal(3, lines[0].FollowingBlankLines);
        Assert.Equal(0, lines[1].FollowingBlankLines);
    }

    [Fact]
    public void Split_WhitespaceOnlyLines_TreatedAsBlank()
    {
        var lines = LineSplitter.Split("first\n   \n\t\nsecond");

        Assert.Equal(2, lines.Count);
        Assert.Equal(2, lines[0].FollowingBlankLines);
    }

    [Fact]
    public void Split_LeadingBlankLines_AreSkipped()
    {
        var lines = LineSplitter.Split("\n\nfirst\nsecond");

        Assert.Equal(2, lines.Count);
        Assert.Equal("first", lines[0].Text);
    }

    [Fact]
    public void Split_TrailingBlankLines_DoNotProduceExtraEntries()
    {
        var lines = LineSplitter.Split("first\n\n\n");

        Assert.Single(lines);
        Assert.Equal("first", lines[0].Text);
        // Trailing blanks after the last non-blank line are recorded on the
        // entry but the segmenter ignores the count for the final line. The
        // contract under test is just that no phantom Line entries appear.
        Assert.Equal(3, lines[0].FollowingBlankLines);
    }
}
