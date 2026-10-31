using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

public class UnsentDraftTests
{
    [Fact]
    public void ClosingWithTextThatWasNotWritten_KeepsItForTheNextSummon_OnlyOnce()
    {
        var draft = new UnsentDraft();

        draft.Close("a long thought", written: false);

        Assert.Equal("a long thought", draft.Take());
        Assert.Equal("", draft.Take());
    }

    [Fact]
    public void ClosingAfterTheTextWasWritten_KeepsNothing()
    {
        var draft = new UnsentDraft();
        draft.Close("old", written: false);

        draft.Close("done", written: true);

        Assert.Equal("", draft.Take());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void ClosingWithBlankText_KeepsNothing(string text)
    {
        var draft = new UnsentDraft();
        draft.Close("old", written: false);

        draft.Close(text, written: false);

        Assert.Equal("", draft.Take());
    }
}
