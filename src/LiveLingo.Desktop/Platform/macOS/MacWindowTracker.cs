using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacWindowTracker : IWindowTracker
{
    public TargetWindowInfo? GetForegroundWindowInfo()
    {
        var (pid, processName) = GetFrontmostApp();
        if (pid <= 0) return null;

        var windowList = CGWindowListCopyWindowInfo(
            kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, 0);

        if (windowList == IntPtr.Zero)
            return new TargetWindowInfo((nint)pid, IntPtr.Zero, processName, "", 0, 0, 800, 600);

        try
        {
            return FindWindowForPid(windowList, pid, processName);
        }
        finally
        {
            CFRelease(windowList);
        }
    }

    private static (int pid, string name) GetFrontmostApp()
    {
        var nsWorkspace = objc_getClass("NSWorkspace");
        var workspace = objc_msgSend(nsWorkspace, sel_registerName("sharedWorkspace"));
        var frontApp = objc_msgSend(workspace, sel_registerName("frontmostApplication"));
        if (frontApp == IntPtr.Zero) return (0, "Unknown");

        var pid = objc_msgSend_int(frontApp, sel_registerName("processIdentifier"));

        var nameObj = objc_msgSend(frontApp, sel_registerName("localizedName"));
        var nameUtf8 = nameObj != IntPtr.Zero
            ? objc_msgSend(nameObj, sel_registerName("UTF8String"))
            : IntPtr.Zero;
        var name = nameUtf8 != IntPtr.Zero
            ? Marshal.PtrToStringUTF8(nameUtf8) ?? "Unknown"
            : "Unknown";

        return (pid, name);
    }

    private static TargetWindowInfo FindWindowForPid(IntPtr windowList, int pid, string processName)
    {
        var count = CFArrayGetCount(windowList);
        var kOwnerPID = CreateCFString("kCGWindowOwnerPID");
        var kWindowName = CreateCFString("kCGWindowName");
        var kWindowBounds = CreateCFString("kCGWindowBounds");
        var kWindowNumber = CreateCFString("kCGWindowNumber");

        try
        {
            for (var i = 0; i < count; i++)
            {
                var dict = CFArrayGetValueAtIndex(windowList, i);

                var pidRef = CFDictionaryGetValue(dict, kOwnerPID);
                if (pidRef == IntPtr.Zero) continue;
                if (!CFNumberGetValue(pidRef, kCFNumberSInt32Type, out int windowPid)) continue;
                if (windowPid != pid) continue;

                var titleRef = CFDictionaryGetValue(dict, kWindowName);
                var title = CFStringToString(titleRef) ?? "";

                int x = 0, y = 0, w = 800, h = 600;
                var boundsRef = CFDictionaryGetValue(dict, kWindowBounds);
                if (boundsRef != IntPtr.Zero &&
                    CGRectMakeWithDictionaryRepresentation(boundsRef, out var rect))
                {
                    x = (int)rect.Origin.X;
                    y = (int)rect.Origin.Y;
                    w = (int)rect.Size.Width;
                    h = (int)rect.Size.Height;
                }

                var windowNumRef = CFDictionaryGetValue(dict, kWindowNumber);
                int windowNumber = 0;
                if (windowNumRef != IntPtr.Zero)
                    CFNumberGetValue(windowNumRef, kCFNumberSInt32Type, out windowNumber);

                return new TargetWindowInfo(
                    (nint)windowNumber, IntPtr.Zero, processName, title,
                    x, y, w, h);
            }
        }
        finally
        {
            CFRelease(kOwnerPID);
            CFRelease(kWindowName);
            CFRelease(kWindowBounds);
            CFRelease(kWindowNumber);
        }

        return new TargetWindowInfo((nint)pid, IntPtr.Zero, processName, "", 0, 0, 800, 600);
    }
}
