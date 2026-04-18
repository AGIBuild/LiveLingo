using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Tests.Translation.Segmentation;

public class SentenceBoundaryTests
{
    [Theory]
    [InlineData('。', true)]
    [InlineData('！', true)]
    [InlineData('？', true)]
    [InlineData('…', true)]
    [InlineData('.', false)]
    [InlineData('!', false)]
    [InlineData('?', false)]
    [InlineData('a', false)]
    [InlineData('，', false)]
    public void IsCjkEnd_OnlyMatchesCjkSentenceMarks(char c, bool expected)
        => Assert.Equal(expected, SentenceBoundary.IsCjkEnd(c));

    [Theory]
    [InlineData('.', true)]
    [InlineData('!', true)]
    [InlineData('?', true)]
    [InlineData('。', false)]
    [InlineData('a', false)]
    public void IsLatinEnd_OnlyMatchesLatinSentenceMarks(char c, bool expected)
        => Assert.Equal(expected, SentenceBoundary.IsLatinEnd(c));

    [Theory]
    [InlineData('.', true)]
    [InlineData('！', true)]
    [InlineData('a', false)]
    [InlineData(' ', false)]
    public void IsAnyEnd_MatchesEitherFamily(char c, bool expected)
        => Assert.Equal(expected, SentenceBoundary.IsAnyEnd(c));

    [Fact]
    public void IsLatinBoundary_AtEndOfText_IsTrue()
        => Assert.True(SentenceBoundary.IsLatinBoundary("Hello.", 5));

    [Fact]
    public void IsLatinBoundary_FollowedByLetter_IsFalse()
    {
        // "U.S.A" — the dots are not boundaries.
        Assert.False(SentenceBoundary.IsLatinBoundary("U.S.A", 1));
        Assert.False(SentenceBoundary.IsLatinBoundary("U.S.A", 3));
    }

    [Fact]
    public void IsLatinBoundary_FollowedByDigit_IsFalse()
    {
        // "3.14" — decimal point.
        Assert.False(SentenceBoundary.IsLatinBoundary("3.14", 1));
    }

    [Fact]
    public void IsLatinBoundary_FollowedBySpaceThenLowercase_IsFalse()
    {
        // "Mr. smith" — abbreviation heuristic.
        Assert.False(SentenceBoundary.IsLatinBoundary("Mr. smith", 2));
    }

    [Fact]
    public void IsLatinBoundary_FollowedBySpaceThenUppercase_IsTrue()
    {
        // "End. Next" — sentence boundary.
        Assert.True(SentenceBoundary.IsLatinBoundary("End. Next", 3));
    }

    [Fact]
    public void IsLatinBoundary_FollowedByNewline_IsTrue()
        => Assert.True(SentenceBoundary.IsLatinBoundary("Done.\nNext", 4));

    [Fact]
    public void IsLatinBoundary_FollowedByTab_AndDigit_IsTrue()
        => Assert.True(SentenceBoundary.IsLatinBoundary("Done.\t9", 4));

    [Fact]
    public void IsLatinBoundary_FollowedByTrailingWhitespaceOnly_IsTrue()
        => Assert.True(SentenceBoundary.IsLatinBoundary("Done.   ", 4));

    [Fact]
    public void EndsWithMark_EmptyOrWhitespace_IsFalse()
    {
        Assert.False(SentenceBoundary.EndsWithMark("".AsSpan()));
        Assert.False(SentenceBoundary.EndsWithMark("   ".AsSpan()));
    }

    [Fact]
    public void EndsWithMark_TrailingMark_IsTrue()
    {
        Assert.True(SentenceBoundary.EndsWithMark("Done.".AsSpan()));
        Assert.True(SentenceBoundary.EndsWithMark("結束。".AsSpan()));
        Assert.True(SentenceBoundary.EndsWithMark("Wow!".AsSpan()));
    }

    [Fact]
    public void EndsWithMark_TrailingWhitespaceIgnored()
        => Assert.True(SentenceBoundary.EndsWithMark("Done.   ".AsSpan()));

    [Fact]
    public void EndsWithMark_NoMark_IsFalse()
        => Assert.False(SentenceBoundary.EndsWithMark("trailing comma,".AsSpan()));

    [Fact]
    public void CountEndings_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0, SentenceBoundary.CountEndings(string.Empty));
        Assert.Equal(0, SentenceBoundary.CountEndings(null!));
    }

    [Fact]
    public void CountEndings_SingleSentence_ReturnsOne()
        => Assert.Equal(1, SentenceBoundary.CountEndings("Hello."));

    [Fact]
    public void CountEndings_AbbreviationsAreNotCounted()
        => Assert.Equal(0, SentenceBoundary.CountEndings("U.S.A"));

    [Fact]
    public void CountEndings_DecimalNumbersAreNotCounted()
        => Assert.Equal(0, SentenceBoundary.CountEndings("Pi is 3.14"));

    [Fact]
    public void CountEndings_ConsecutiveMarksCountedOnce()
    {
        Assert.Equal(1, SentenceBoundary.CountEndings("Wow?!"));
        Assert.Equal(1, SentenceBoundary.CountEndings("Wait..."));
    }

    [Fact]
    public void CountEndings_CjkSentences_AreCounted()
        => Assert.Equal(2, SentenceBoundary.CountEndings("你好。世界！"));

    [Fact]
    public void CountEndings_MixedCjkLatin_AreSummed()
        => Assert.Equal(2, SentenceBoundary.CountEndings("你好。 Hello world."));

    [Fact]
    public void CountEndings_RunOfMixedCjkLatinMarks_CountedOnce()
    {
        // "?。!" — three sentence-end marks in a row. The absorption loop
        // must skip past every consecutive mark regardless of family. If
        // the i++ inside the loop were dropped, we would re-count the same
        // run on every iteration of the outer loop.
        Assert.Equal(1, SentenceBoundary.CountEndings("done?。!"));
    }

    [Fact]
    public void CountEndings_RunOfMarksThenAnotherSentence_CountedTwice()
    {
        // After the absorption skips past "?!", the outer loop must resume
        // from the correct offset so it still finds the trailing "."
        // boundary for "Yes."
        Assert.Equal(2, SentenceBoundary.CountEndings("Wait?! Yes."));
    }
}
