namespace LiveLingo.Desktop.Platform;

public interface IPlatformServices : IDisposable
{
    IHotkeyService Hotkey { get; }
    IWindowTracker WindowTracker { get; }
    ITextInjector TextInjector { get; }
    IClipboardService Clipboard { get; }
    IAudioCaptureService AudioCapture { get; }
}
