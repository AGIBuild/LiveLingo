namespace LiveLingo.Desktop.Platform;

public static class HotkeyParser
{
    public static HotkeyBinding Parse(string id, string hotkeyString)
    {
        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = KeyModifiers.None;
        var key = string.Empty;

        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL" or "CONTROL":
                    modifiers |= KeyModifiers.Ctrl;
                    break;
                case "ALT" or "OPTION":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "CMD" or "COMMAND" or "WIN" or "META" or "SUPER":
                    modifiers |= KeyModifiers.Meta;
                    break;
                default:
                    key = upper;
                    break;
            }
        }

        if (string.IsNullOrEmpty(key))
            throw new ArgumentException($"Hotkey string '{hotkeyString}' does not contain a key", nameof(hotkeyString));

        return new HotkeyBinding(id, modifiers, key);
    }
}
