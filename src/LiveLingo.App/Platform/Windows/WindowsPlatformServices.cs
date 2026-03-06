namespace LiveLingo.App.Platform.Windows;

internal sealed class WindowsPlatformServices : IPlatformServices
{
    public IHotkeyService Hotkey { get; }
    public IWindowTracker WindowTracker { get; }
    public ITextInjector TextInjector { get; }
    public IClipboardService Clipboard { get; }

    public WindowsPlatformServices()
    {
        Clipboard = new Win32ClipboardService();
        Hotkey = new Win32HotkeyService();
        WindowTracker = new Win32WindowTracker();
        TextInjector = new Win32TextInjector(Clipboard);
    }

    public void Dispose()
    {
        Hotkey.Dispose();
    }
}
