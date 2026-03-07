using System.Runtime.Versioning;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
public static class AccessibilityPermission
{
    public static bool IsGranted() =>
        AXIsProcessTrustedWithOptions(IntPtr.Zero);

    public static bool RequestAndCheck()
    {
        var promptKey = GetAXTrustedCheckOptionPrompt();
        var kTrue = GetCFBooleanTrue();
        var options = CFDictionaryCreateMutable(IntPtr.Zero, 1, IntPtr.Zero, IntPtr.Zero);
        CFDictionarySetValue(options, promptKey, kTrue);
        try
        {
            return AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            CFRelease(options);
        }
    }

    /// <summary>
    /// Tests Input Monitoring permission by attempting to create and immediately
    /// release a listen-only CGEventTap. Returns false if macOS blocks the tap.
    /// </summary>
    public static bool IsInputMonitoringGranted()
    {
        CGEventTapCallBack cb = (_, _, ev, _) => ev;
        var mask = 1UL << (int)kCGEventKeyDown;
        var tap = CGEventTapCreate(
            kCGSessionEventTap, kCGHeadInsertEventTap,
            kCGEventTapOptionListenOnly, mask, cb, IntPtr.Zero);
        GC.KeepAlive(cb);
        if (tap == IntPtr.Zero) return false;
        CFRelease(tap);
        return true;
    }
}
