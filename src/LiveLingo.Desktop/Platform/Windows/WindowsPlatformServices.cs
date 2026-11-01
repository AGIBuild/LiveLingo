using System.Runtime.Versioning;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }
    public IMicrophone Microphone { get; } = new PortAudioMicrophone();
    public IMicrophonePermission MicrophonePermission { get; } = new WindowsMicrophonePermission();

    public WindowsPlatformServices()
    {
        Clipboard = new Win32ClipboardService();
        Hotkey = new Win32HotkeyService();
        WindowTracker = new Win32WindowTracker();
        TextInjector = new Win32TextInjector(Clipboard, WindowTracker);
    }

    public void OpenUrl(string url) => BrowserLauncher.OpenUrl(url);

    public void Dispose()
    {
        Microphone.Dispose();
        Hotkey.Dispose();
    }
}
