using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacHotkeyService : IHotkeyService
{
    public event Action<HotkeyEventArgs>? HotkeyTriggered;

    public void Register(HotkeyBinding binding)
    {
        // TODO P4: CGEventTapCreate on background thread with CFRunLoop
        throw new PlatformNotSupportedException("macOS hotkey service requires CGEventTap implementation");
    }

    public void Unregister(string hotkeyId)
    {
        // TODO P4: Disable event tap for this binding
    }

    public void Dispose()
    {
        // TODO P4: Disable tap, stop run loop, release CF objects
    }
}
