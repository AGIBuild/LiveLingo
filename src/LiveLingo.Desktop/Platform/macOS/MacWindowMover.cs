using System.Runtime.Versioning;
using Avalonia.Platform;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

/// <summary>
/// <c>-[NSWindow performWindowDragWithEvent:]</c> with the event the application is handling: the mouse-dragged event
/// that made the page report the drag. This is how other embedded-web-view shells move their windows.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowMover : IWindowMover
{
    /// <summary>Bit 0 of <c>+[NSEvent pressedMouseButtons]</c>.</summary>
    private const int LeftButton = 1;

    public void BeginDrag(IPlatformHandle window)
    {
        if (window is not IMacOSTopLevelPlatformHandle { NSWindow: not 0 } top)
            throw new InvalidOperationException("The window has no NSWindow to drag.");

        // The page reports the drag while the button is held, but the report travels: when the button was released on
        // the way there is no drag to start (the system's drag loop would wait for a release that already happened).
        var pressed = objc_msgSend_int(objc_getClass("NSEvent"), sel_registerName("pressedMouseButtons"));
        if ((pressed & LeftButton) == 0)
            return;

        var application = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var current = objc_msgSend(application, sel_registerName("currentEvent"));
        if (current == IntPtr.Zero)
            throw new InvalidOperationException("There is no mouse event to start the window drag from.");

        objc_msgSend(top.NSWindow, sel_registerName("performWindowDragWithEvent:"), current);
    }
}
