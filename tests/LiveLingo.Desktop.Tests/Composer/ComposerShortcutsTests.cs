using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerShortcutsTests
{
    [Fact]
    public void EveryShortcut_HasItsOwnIdAndItsOwnKeyCombination()
    {
        var all = ComposerShortcuts.All;

        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(s => (s.Code, s.Shift)).Distinct().Count());
    }

    [Fact]
    public void NoShortcutTakesAKeyThatEditingOrTheSystemNeeds()
    {
        // Command/Ctrl + these is cut, paste, copy, select all, undo, redo, quit, hide, minimize, close, reload.
        string[] reserved = ["KeyX", "KeyV", "KeyA", "KeyZ", "KeyY", "KeyQ", "KeyH", "KeyM", "KeyW", "KeyF"];

        Assert.DoesNotContain(ComposerShortcuts.All, s => reserved.Contains(s.Code));
        // Command/Ctrl+C is copy of the selected text; only the shifted combination belongs to the composer.
        Assert.All(ComposerShortcuts.All.Where(s => s.Code == "KeyC"), s => Assert.True(s.Shift));
    }

    [Theory]
    [InlineData("dictate", true, "⌘D")]
    [InlineData("dictate", false, "Ctrl+D")]
    [InlineData("copy", true, "⇧⌘C")]
    [InlineData("copy", false, "Ctrl+Shift+C")]
    [InlineData("settings", true, "⌘,")]
    public void Cap_FollowsTheConventionsOfTheOperatingSystem(string id, bool mac, string expected)
    {
        Assert.Equal(expected, ComposerShortcuts.Cap(ComposerShortcuts.Of(id), mac));
    }

    [Fact]
    public void AnUnknownShortcut_IsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => ComposerShortcuts.Of("fly"));
    }
}
