using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken ct)
    {
        // TODO P4: NSPasteboard generalPasteboard → clearContents + setString:forType:
        throw new PlatformNotSupportedException("macOS clipboard requires NSPasteboard implementation");
    }

    public Task<string?> GetTextAsync(CancellationToken ct)
    {
        // TODO P4: NSPasteboard generalPasteboard → stringForType:NSPasteboardTypeString
        throw new PlatformNotSupportedException("macOS clipboard requires NSPasteboard implementation");
    }
}
