namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class WindowsPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }
    public IAudioCaptureService AudioCapture { get; }

    public WindowsPlatformServices()
    {
        Clipboard = new Win32ClipboardService();
        Hotkey = new Win32HotkeyService();
        WindowTracker = new Win32WindowTracker();
        TextInjector = new Win32TextInjector(Clipboard);
        AudioCapture = new Win32AudioCaptureService();
    }

    public void Dispose()
    {
        Hotkey.Dispose();
        AudioCapture.Dispose();
    }
}
