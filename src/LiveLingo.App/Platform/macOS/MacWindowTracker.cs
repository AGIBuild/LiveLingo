using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacWindowTracker : IWindowTracker
{
    public TargetWindowInfo? GetForegroundWindowInfo()
    {
        // TODO P4: NSWorkspace.frontmostApplication + CGWindowListCopyWindowInfo
        throw new PlatformNotSupportedException("macOS window tracker requires NSWorkspace implementation");
    }
}
