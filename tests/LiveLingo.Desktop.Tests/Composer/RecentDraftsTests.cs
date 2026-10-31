using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

public class RecentDraftsTests
{
    private static RecentDrafts Create(out FakeClock clock)
    {
        clock = new FakeClock();
        return new RecentDrafts(clock);
    }

    [Fact]
    public void ClosingWithTextThatWasNotWritten_KeepsItForTheNextSummon_OnlyOnce()
    {
        var drafts = Create(out _);

        drafts.Close("a long thought", written: false);

        Assert.Equal("a long thought", drafts.TakeUnsent());
        Assert.Equal("", drafts.TakeUnsent());
        Assert.Empty(drafts.Items);
    }

    [Fact]
    public void AWrittenDraft_IsListedButNotRestoredAutomatically()
    {
        var drafts = Create(out _);

        drafts.Close("done", written: true);

        Assert.Equal("", drafts.TakeUnsent());
        Assert.Equal(["done"], drafts.Items.Select(d => d.Text).ToArray());
    }

    [Fact]
    public void OnlyTheLatestDraftCanBeRestoredAutomatically()
    {
        var drafts = Create(out _);
        drafts.Close("older unsent", written: false);
        drafts.Close("newer written", written: true);

        Assert.Equal("", drafts.TakeUnsent());
        Assert.Equal(2, drafts.Items.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void BlankTextIsNeverKept(string text)
    {
        var drafts = Create(out _);

        drafts.Close(text, written: false);

        Assert.Empty(drafts.Items);
    }

    [Fact]
    public void ItemsAreNewestFirst_LimitedToTheCapacity_AndStampedWithTheTime()
    {
        var drafts = Create(out var clock);
        for (var i = 1; i <= RecentDrafts.Capacity + 2; i++)
        {
            drafts.Close($"draft {i}", written: true);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(RecentDrafts.Capacity, drafts.Items.Count);
        Assert.Equal("draft 7", drafts.Items[0].Text);
        Assert.Equal("draft 3", drafts.Items[^1].Text);
        Assert.True(drafts.Items[0].At > drafts.Items[1].At);
    }

    [Fact]
    public void TheSameTextTwice_ReplacesTheOlderEntry()
    {
        var drafts = Create(out _);
        drafts.Close("same", written: false);
        drafts.Close("other", written: true);

        drafts.Close("same", written: true);

        Assert.Equal(["same", "other"], drafts.Items.Select(d => d.Text).ToArray());
        Assert.True(drafts.Items[0].Written);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var drafts = Create(out _);
        drafts.Close("x", written: false);

        drafts.Clear();

        Assert.Empty(drafts.Items);
        Assert.Equal("", drafts.TakeUnsent());
    }
}
