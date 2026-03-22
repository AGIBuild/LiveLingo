using System.Runtime.Versioning;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }
    public IAudioCaptureService AudioCapture { get; }

    public MacPlatformServices()
    {
        Clipboard = new MacClipboardService();
        Hotkey = new MacHotkeyService();
        WindowTracker = new MacWindowTracker();
        TextInjector = new MacTextInjector(Clipboard);
        AudioCapture = new MacAudioCaptureService();
    }

    public void OpenUrl(string url) => BrowserLauncher.OpenUrl(url);

    public void Dispose()
    {
        Hotkey.Dispose();
        AudioCapture.Dispose();
    }
}
