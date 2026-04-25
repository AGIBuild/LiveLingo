using LiveLingo.Core.Translation;
using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Tests.Translation.Segmentation;

public class UnitPlannerTests
{
    [Fact]
    public void Plan_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(UnitPlanner.Plan([], 100));
    }

    [Fact]
    public void Plan_NullInput_ReturnsEmpty()
    {
        // Defensive guard: mutating "||" → "&&" inside the null-or-empty
        // check would NRE on the .Count call. Direct null call proves the
        // guard short-circuits on null without dereferencing.
        Assert.Empty(UnitPlanner.Plan(null!, 100));
    }

    [Fact]
    public void Plan_SingleSegment_ReturnsOneUnit()
    {
        var segments = new[] { new TextSegment("hello", SegmentBreak.None) };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Single(units);
        Assert.Equal("hello", units[0].SourceText);
        Assert.Equal(0, units[0].FirstSegmentIndex);
        Assert.Equal(1, units[0].SegmentCount);
        Assert.Equal(SegmentBreak.None, units[0].BreakAfter);
    }

    [Fact]
    public void Plan_LineConnectedSegments_AreMergedIntoOneUnit()
    {
        var segments = new[]
        {
            new TextSegment("first line", SegmentBreak.Line),
            new TextSegment("second line", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Single(units);
        Assert.Equal("first line\nsecond line", units[0].SourceText);
        Assert.Equal(2, units[0].SegmentCount);
        Assert.Equal(SegmentBreak.None, units[0].BreakAfter);
    }

    [Fact]
    public void Plan_RunningCombinedLengthDecidesSplit_NotFirstSegmentLengthAlone()
    {
        // Three line-connected segments:
        //   a (1) + '\n' (1) + b (1)            = 3   → fits in budget 5
        //   then "a\nb" (3) + '\n' (1) + c (3)  = 7   → exceeds budget 5
        // The combined-length tracker must include the second segment, not
        // just the first, otherwise the split point moves to the wrong place.
        var segments = new[]
        {
            new TextSegment("a",   SegmentBreak.Line),
            new TextSegment("b",   SegmentBreak.Line),
            new TextSegment("ccc", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 5);

        Assert.Equal(2, units.Count);
        Assert.Equal("a\nb", units[0].SourceText);
        Assert.Equal("ccc", units[1].SourceText);
    }

    [Fact]
    public void Plan_SentenceConnectedSegments_StayAsSeparateUnits()
    {
        var segments = new[]
        {
            new TextSegment("Hello.", SegmentBreak.Sentence),
            new TextSegment("World.", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
        Assert.Equal("Hello.", units[0].SourceText);
        Assert.Equal("World.", units[1].SourceText);
    }

    [Fact]
    public void Plan_ParagraphConnectedSegments_StayAsSeparateUnits()
    {
        var segments = new[]
        {
            new TextSegment("Para one", SegmentBreak.Paragraph),
            new TextSegment("Para two", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
        Assert.Equal(SegmentBreak.Paragraph, units[0].BreakAfter);
    }

    [Fact]
    public void Plan_LineConnectedButFirstEndsWithSentenceMark_StayAsSeparateUnits()
    {
        // A line that ends in "." is a complete thought even when followed by
        // another line — do not merge.
        var segments = new[]
        {
            new TextSegment("First sentence.", SegmentBreak.Line),
            new TextSegment("Second sentence", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
        Assert.Equal("First sentence.", units[0].SourceText);
    }

    [Fact]
    public void Plan_LineConnectedButCjkSentenceMark_StayAsSeparateUnits()
    {
        var segments = new[]
        {
            new TextSegment("第一句。", SegmentBreak.Line),
            new TextSegment("第二句", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void Plan_AddingNextWouldOverflow_StartsNewUnit()
    {
        // Line-connected pair, but adding the second would exceed the limit.
        var segments = new[]
        {
            new TextSegment(new string('a', 50), SegmentBreak.Line),
            new TextSegment(new string('b', 50), SegmentBreak.None)
        };
        // Limit: 50 + 1 + 50 = 101, so a 100 budget must split them.
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void Plan_AddingNextExactlyAtLimit_FitsInSameUnit()
    {
        var segments = new[]
        {
            new TextSegment(new string('a', 49), SegmentBreak.Line),
            new TextSegment(new string('b', 50), SegmentBreak.None)
        };
        // 49 + 1 + 50 = 100 → exactly at limit.
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Single(units);
        Assert.Equal(2, units[0].SegmentCount);
    }

    [Fact]
    public void Plan_ThreeLineConnectedSegments_AllFitInOneUnit()
    {
        var segments = new[]
        {
            new TextSegment("a", SegmentBreak.Line),
            new TextSegment("b", SegmentBreak.Line),
            new TextSegment("c", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Single(units);
        Assert.Equal("a\nb\nc", units[0].SourceText);
        Assert.Equal(3, units[0].SegmentCount);
    }

    [Fact]
    public void Plan_RecordsCorrectFirstSegmentIndex()
    {
        var segments = new[]
        {
            new TextSegment("a.", SegmentBreak.Sentence),
            new TextSegment("b.", SegmentBreak.Sentence),
            new TextSegment("c.", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(3, units.Count);
        Assert.Equal(0, units[0].FirstSegmentIndex);
        Assert.Equal(1, units[1].FirstSegmentIndex);
        Assert.Equal(2, units[2].FirstSegmentIndex);
    }

    [Fact]
    public void Plan_PropagatesBreakAfterFromLastSegmentInUnit()
    {
        var segments = new[]
        {
            new TextSegment("a", SegmentBreak.Line),
            new TextSegment("b", SegmentBreak.Paragraph),
            new TextSegment("c", SegmentBreak.None)
        };
        var units = UnitPlanner.Plan(segments, 100);

        Assert.Equal(2, units.Count);
        Assert.Equal(SegmentBreak.Paragraph, units[0].BreakAfter);
        Assert.Equal(SegmentBreak.None, units[1].BreakAfter);
    }
}
