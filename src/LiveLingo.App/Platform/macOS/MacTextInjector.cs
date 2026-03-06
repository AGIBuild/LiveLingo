using System.Runtime.Versioning;

namespace LiveLingo.App.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacTextInjector : ITextInjector
{
    private readonly IClipboardService _clipboard;

    public MacTextInjector(IClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    public Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        // TODO P4: AXUIElement text insertion → fallback to Cmd+V paste via CGEventPost
        throw new PlatformNotSupportedException("macOS text injector requires AXUIElement implementation");
    }
}
