using System.Runtime.Versioning;
using Serilog;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacTextInjector : ITextInjector
{
    private readonly IClipboardService _clipboard;
    private readonly IWindowTracker _windows;

    public MacTextInjector(IClipboardService clipboard, IWindowTracker windows)
    {
        _clipboard = clipboard;
        _windows = windows;
    }

    public async Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        Log.Information("InjectAsync: autoSend={AutoSend}, target={Process}, textLen={Len}",
            autoSend, target.ProcessName, text.Length);

        _windows.Activate(target);
        await Task.Delay(100, ct);

        await _clipboard.SetTextAsync(text, ct);
        await Task.Delay(80, ct);

        ct.ThrowIfCancellationRequested();
        PostKeystroke(kVK_V, kCGEventFlagMaskCommand);
        Log.Debug("Posted Cmd+V");

        if (autoSend)
        {
            await Task.Delay(300, ct);
            ct.ThrowIfCancellationRequested();
            PostKeystroke(kVK_Return, flags: 0);
            Log.Debug("Posted Return (send)");
        }
    }

    private static void PostKeystroke(ushort keycode, ulong flags)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
        if (flags != 0) CGEventSetFlags(down, flags);
        CGEventPost(0, down);
        CFRelease(down);

        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, false);
        if (flags != 0) CGEventSetFlags(up, flags);
        CGEventPost(0, up);
        CFRelease(up);
    }
}
