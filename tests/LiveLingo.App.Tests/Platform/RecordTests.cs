using LiveLingo.App.Platform;

namespace LiveLingo.App.Tests.Platform;

public class RecordTests
{
    [Fact]
    public void TargetWindowInfo_RecordEquality()
    {
        var a = new TargetWindowInfo(1, 2, "slack", "Slack", 0, 0, 800, 600);
        var b = new TargetWindowInfo(1, 2, "slack", "Slack", 0, 0, 800, 600);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TargetWindowInfo_Inequality()
    {
        var a = new TargetWindowInfo(1, 2, "slack", "Slack", 0, 0, 800, 600);
        var b = new TargetWindowInfo(3, 4, "teams", "Teams", 100, 100, 1024, 768);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HotkeyBinding_RecordEquality()
    {
        var a = new HotkeyBinding("overlay", KeyModifiers.Ctrl | KeyModifiers.Alt, "T");
        var b = new HotkeyBinding("overlay", KeyModifiers.Ctrl | KeyModifiers.Alt, "T");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HotkeyEventArgs_RecordEquality()
    {
        var a = new HotkeyEventArgs("overlay");
        var b = new HotkeyEventArgs("overlay");
        Assert.Equal(a, b);
    }

    [Fact]
    public void KeyModifiers_FlagsWork()
    {
        var mods = KeyModifiers.Ctrl | KeyModifiers.Alt;
        Assert.True(mods.HasFlag(KeyModifiers.Ctrl));
        Assert.True(mods.HasFlag(KeyModifiers.Alt));
        Assert.False(mods.HasFlag(KeyModifiers.Shift));
        Assert.False(mods.HasFlag(KeyModifiers.Meta));
    }

    [Fact]
    public void KeyModifiers_None_HasNoFlags()
    {
        var mods = KeyModifiers.None;
        Assert.False(mods.HasFlag(KeyModifiers.Ctrl));
        Assert.False(mods.HasFlag(KeyModifiers.Alt));
    }
}
