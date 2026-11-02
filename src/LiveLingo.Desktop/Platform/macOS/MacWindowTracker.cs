using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacWindowTracker : IWindowTracker
{
    /// <summary>The frontmost app's top window, or null when it has none: a target always has a real window number.</summary>
    public TargetWindowInfo? GetForegroundWindowInfo()
    {
        var (pid, processName, appId) = GetFrontmostApp();
        // Our own parked composer is never a target: it would be placed relative to itself, off screen.
        if (pid <= 0 || pid == Environment.ProcessId) return null;

        var windowList = CGWindowListCopyWindowInfo(
            kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, 0);

        if (windowList == IntPtr.Zero)
            return null;

        try
        {
            return FindWindowForPid(windowList, pid, processName, appId);
        }
        finally
        {
            CFRelease(windowList);
        }
    }

    public bool IsAlive(TargetWindowInfo target)
    {
        var windowList = CGWindowListCopyWindowInfo(kCGWindowListOptionAll, 0);
        if (windowList == IntPtr.Zero)
            return false;

        var kWindowNumber = CreateCFString("kCGWindowNumber");
        try
        {
            var count = CFArrayGetCount(windowList);
            for (var i = 0; i < count; i++)
            {
                var number = CFDictionaryGetValue(CFArrayGetValueAtIndex(windowList, i), kWindowNumber);
                if (number != IntPtr.Zero && CFNumberGetValue(number, kCFNumberSInt32Type, out int windowNumber)
                    && windowNumber == (int)target.Handle)
                    return true;
            }
            return false;
        }
        finally
        {
            CFRelease(kWindowNumber);
            CFRelease(windowList);
        }
    }

    public void Activate(TargetWindowInfo target)
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

    private static (int pid, string name, string appId) GetFrontmostApp()
    {
        var nsWorkspace = objc_getClass("NSWorkspace");
        var workspace = objc_msgSend(nsWorkspace, sel_registerName("sharedWorkspace"));
        var frontApp = objc_msgSend(workspace, sel_registerName("frontmostApplication"));
        if (frontApp == IntPtr.Zero) return (0, "Unknown", "");

        var pid = objc_msgSend_int(frontApp, sel_registerName("processIdentifier"));

        var name = ObjCString(objc_msgSend(frontApp, sel_registerName("localizedName"))) ?? "Unknown";
        // A process that is not an application bundle has no identifier; it is then known by its name.
        var appId = ObjCString(objc_msgSend(frontApp, sel_registerName("bundleIdentifier"))) ?? name;

        return (pid, name, appId);
    }

    private static string? ObjCString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return null;

        var utf8 = objc_msgSend(nsString, sel_registerName("UTF8String"));
        return utf8 != IntPtr.Zero ? Marshal.PtrToStringUTF8(utf8) : null;
    }

    private static TargetWindowInfo? FindWindowForPid(IntPtr windowList, int pid, string processName, string appId)
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
                if (windowNumRef == IntPtr.Zero || !CFNumberGetValue(windowNumRef, kCFNumberSInt32Type, out windowNumber))
                    continue;

                return new TargetWindowInfo(
                    (nint)windowNumber, IntPtr.Zero, processName, title,
                    x, y, w, h, appId);
            }
        }
        finally
        {
            CFRelease(kOwnerPID);
            CFRelease(kWindowName);
            CFRelease(kWindowBounds);
            CFRelease(kWindowNumber);
        }

        return null;
    }
}
