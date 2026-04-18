using LiveLingo.Core.Translation.Segmentation;

namespace LiveLingo.Core.Tests.Translation.Segmentation;

public class SentenceSplitterTests
{
    [Fact]
    public void Split_Empty_ReturnsEmpty()
        => Assert.Empty(SentenceSplitter.Split(string.Empty));

    [Fact]
    public void Split_NoSentenceMark_ReturnsWholeText()
    {
        var result = SentenceSplitter.Split("just a phrase");

        Assert.Single(result);
        Assert.Equal("just a phrase", result[0]);
    }

    [Fact]
    public void Split_SingleSentenceWithTrailingMark_ReturnsSingleEntry()
    {
        var result = SentenceSplitter.Split("Hello world.");

        Assert.Single(result);
        Assert.Equal("Hello world.", result[0]);
    }

    [Fact]
    public void Split_TwoLatinSentences_AreSplit()
    {
        var result = SentenceSplitter.Split("Hello. World!");

        Assert.Equal(2, result.Count);
        Assert.Equal("Hello.", result[0]);
        Assert.Equal("World!", result[1]);
    }

    [Fact]
    public void Split_TwoCjkSentences_AreSplit()
    {
        var result = SentenceSplitter.Split("你好。世界！");

        Assert.Equal(2, result.Count);
        Assert.Equal("你好。", result[0]);
        Assert.Equal("世界！", result[1]);
    }

    [Fact]
    public void Split_MixedCjkLatinSentencesOnSameLine_AreSplit()
    {
        // The exact "missing trailing sentence" bug from the original report.
        var result = SentenceSplitter.Split("你好啊，胆小鬼。 你是不是不知道我是谁？");

        Assert.Equal(2, result.Count);
        Assert.Equal("你好啊，胆小鬼。", result[0]);
        Assert.Equal("你是不是不知道我是谁？", result[1]);
    }

    [Fact]
    public void Split_AbbreviationFollowedByLowercase_DoesNotSplit()
    {
        // The Latin boundary heuristic only treats "." as a sentence end when
        // the next non-whitespace char is NOT lowercase. "Mr. smith" therefore
        // stays as one sentence. (The classic "Mr. Smith" case is intentionally
        // left as a known false positive; uppercase follow-words are far more
        // common as real sentence boundaries than as abbreviation continuations.)
        var result = SentenceSplitter.Split("Mr. smith arrived.");

        Assert.Single(result);
        Assert.Equal("Mr. smith arrived.", result[0]);
    }

    [Fact]
    public void Split_DecimalNumberDoesNotSplit()
    {
        var result = SentenceSplitter.Split("Pi is 3.14 here.");

        Assert.Single(result);
    }

    [Fact]
    public void Split_ConsecutiveMarksAreAttachedToPrecedingSentence()
    {
        var result = SentenceSplitter.Split("Really?! Yes.");

        Assert.Equal(2, result.Count);
        Assert.Equal("Really?!", result[0]);
        Assert.Equal("Yes.", result[1]);
    }

    [Fact]
    public void Split_EllipsisStaysAttached()
    {
        var result = SentenceSplitter.Split("Wait... Then go.");

        Assert.Equal(2, result.Count);
        Assert.Equal("Wait...", result[0]);
        Assert.Equal("Then go.", result[1]);
    }

    [Fact]
    public void Split_TabsBetweenSentences_AreConsumed()
    {
        var result = SentenceSplitter.Split("Hello.\tWorld!");

        Assert.Equal(2, result.Count);
        Assert.Equal("Hello.", result[0]);
        Assert.Equal("World!", result[1]);
    }

    [Fact]
    public void Split_MultipleSpacesAndTabsBetweenSentences_AreFullyConsumed()
    {
        // Mutating the inter-sentence "is space OR is tab" check to AND would
        // make the loop never advance, so the second sentence would carry the
        // trailing whitespace as its prefix. Direct assertion exposes that.
        var result = SentenceSplitter.Split("End. \t  \tNext sentence.");

        Assert.Equal(2, result.Count);
        Assert.Equal("End.", result[0]);
        Assert.Equal("Next sentence.", result[1]);
    }

    [Fact]
    public void Split_TrailingWhitespaceTrimmedFromEachSentence()
    {
        // Uppercase follow-letter so the Latin boundary heuristic accepts the split.
        var result = SentenceSplitter.Split("A.   B.   ");

        Assert.Equal(2, result.Count);
        Assert.Equal("A.", result[0]);
        Assert.Equal("B.", result[1]);
    }
}
