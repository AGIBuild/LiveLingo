using System.Runtime.Versioning;
using Serilog;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacTextInjector : ITextInjector
{
    private readonly IClipboardService _clipboard;

    public MacTextInjector(IClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    public async Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        Log.Information("InjectAsync: autoSend={AutoSend}, target={Process}, textLen={Len}",
            autoSend, target.ProcessName, text.Length);

        ActivateProcess(target);
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

    private static void ActivateProcess(TargetWindowInfo target)
    {
        if (string.IsNullOrEmpty(target.ProcessName)) return;

        var workspace = objc_msgSend(
            objc_getClass("NSWorkspace"),
            sel_registerName("sharedWorkspace"));
        var apps = objc_msgSend(workspace, sel_registerName("runningApplications"));
        var count = objc_msgSend_int(apps, sel_registerName("count"));

        for (var i = 0; i < count; i++)
        {
            var app = objc_msgSend(apps, sel_registerName("objectAtIndex:"), (IntPtr)i);
            var namePtr = objc_msgSend(app, sel_registerName("localizedName"));
            var name = CFStringToString(namePtr);

            if (name is not null && target.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                objc_msgSend(app,
                    sel_registerName("activateWithOptions:"),
                    (IntPtr)3); // NSApplicationActivateIgnoringOtherApps | NSApplicationActivateAllWindows
                Log.Debug("Activated target process: {Name}", name);
                return;
            }
        }

        Log.Debug("Could not find target process to activate: {Name}", target.ProcessName);
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
