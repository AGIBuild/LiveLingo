namespace LiveLingo.Desktop.Platform;

public interface IPlatformServices : IDisposable
{
    IHotkeyService Hotkey { get; }
    IWindowTracker WindowTracker { get; }
    ITextInjector TextInjector { get; }
    IClipboardService Clipboard { get; }
    IAudioCaptureService AudioCapture { get; }

    /// <summary>
    /// Opens an https URL in the system default browser.
    /// </summary>
    void OpenUrl(string url);
}
