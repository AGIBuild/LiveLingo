using LiveLingo.Core.Transcription;

namespace LiveLingo.Core.Tests.Transcription;

public class SrtWriterTests
{
    [Fact]
    public void WritesNumberedCuesWithCommaMilliseconds()
    {
        var srt = SrtWriter.Write([
            new TimedSegment(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3), "你好"),
            new TimedSegment(new TimeSpan(1, 2, 3) + TimeSpan.FromMilliseconds(45), new TimeSpan(1, 2, 4), "second\nline")
        ]);

        Assert.Equal(
            "1\n00:00:01,500 --> 00:00:03,000\n你好\n\n2\n01:02:03,045 --> 01:02:04,000\nsecond\nline\n\n",
            srt);
    }

    [Fact]
    public void SkipsCuesWithoutTextAndKeepsNumbersContiguous()
    {
        var srt = SrtWriter.Write([
            new TimedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a"),
            new TimedSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "  "),
            new TimedSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "c")
        ]);

        Assert.Contains("2\n00:00:02,000 --> 00:00:03,000\nc", srt);
        Assert.DoesNotContain("3\n", srt);
    }

    [Fact]
    public void AnEmptyList_WritesNothing() => Assert.Equal("", SrtWriter.Write([]));

    [Fact]
    public void BlankLinesInsideATextAreDropped_SoTheyDoNotEndTheCue()
    {
        var srt = SrtWriter.Write([new TimedSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "a\n\nb")]);

        Assert.Equal("1\n00:00:00,000 --> 00:00:01,000\na\nb\n\n", srt);
    }
}
