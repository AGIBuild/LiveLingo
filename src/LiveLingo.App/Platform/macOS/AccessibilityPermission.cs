using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
public static class AccessibilityPermission
{
    public static bool IsGranted()
    {
        // TODO P4: AXIsProcessTrustedWithOptions
        return false;
    }

    public static bool RequestAndCheck()
    {
        // TODO P4: AXIsProcessTrustedWithOptions with prompt option
        return false;
    }
}
