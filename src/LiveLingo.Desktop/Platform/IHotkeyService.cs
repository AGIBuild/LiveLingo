namespace LiveLingo.Desktop.Platform;

public interface IHotkeyService : IDisposable
{
    event Action<HotkeyEventArgs>? HotkeyTriggered;
    void Register(HotkeyBinding binding);
    void Unregister(string hotkeyId);
}

public record HotkeyBinding(string Id, KeyModifiers Modifiers, string Key);
public record HotkeyEventArgs(string HotkeyId);

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}
