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
}
