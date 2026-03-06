using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }

    public MacPlatformServices()
    {
        Clipboard = new MacClipboardService();
        Hotkey = new MacHotkeyService();
        WindowTracker = new MacWindowTracker();
        TextInjector = new MacTextInjector(Clipboard);
    }

    public void Dispose()
    {
        Hotkey.Dispose();
    }
}
