namespace LiveLingo.Desktop.Composer;

/// <param name="Id">What it does; also the id of the menu item or button it belongs to.</param>
/// <param name="Code">The physical key (<c>KeyboardEvent.code</c>), so it works with any keyboard layout and input method.</param>
/// <param name="Key">The key as printed on a key cap.</param>
/// <param name="Shift">Pressed together with Shift.</param>
public sealed record Shortcut(string Id, string Code, string Key, bool Shift);

/// <summary>
/// The keyboard shortcuts of the composer window, the only place they are defined: the page handles the keys and
/// shows the caps from the snapshot, the texts show them in hints. Each is Command (macOS) or Ctrl (Windows) plus the
/// key; Enter, Shift+Enter and Esc keep their meanings of the writing flow and are not listed here.
/// </summary>
public static class ComposerShortcuts
{
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new("dictate", "KeyD", "D", false),
        new("clean", "Digit1", "1", false),
        new("translate", "Digit2", "2", false),
        new("original", "Digit3", "3", false),
        new("swap", "KeyL", "L", false),
        new("copy", "KeyC", "C", true),
        new("new", "KeyN", "N", false),
        new("recent", "KeyR", "R", false),
        new("import", "KeyO", "O", false),
        new("export", "KeyS", "S", false),
        new("settings", "Comma", ",", false)
    ];

    public static Shortcut Of(string id) => All.Single(shortcut => shortcut.Id == id);

    /// <summary>"⌘D" and "⇧⌘C" on macOS, "Ctrl+D" and "Ctrl+Shift+C" elsewhere.</summary>
    public static string Cap(Shortcut shortcut, bool mac) => mac
        ? (shortcut.Shift ? "⇧⌘" : "⌘") + shortcut.Key
        : (shortcut.Shift ? "Ctrl+Shift+" : "Ctrl+") + shortcut.Key;
}
