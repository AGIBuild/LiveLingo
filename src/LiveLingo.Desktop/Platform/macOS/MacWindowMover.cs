using System.Runtime.Versioning;
using Avalonia.Platform;
using Avalonia.Threading;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

/// <summary>
/// <c>-[NSWindow performWindowDragWithEvent:]</c> with the event the application is handling: the mouse-dragged event
/// that made the page report the drag. Unlike Win32's modal caption move loop, AppKit hands the drag to WindowServer
/// and returns right away, so we wait until the global mouse button is released and let the final frame notification
/// land before reporting that the drag ended.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindowMover : IWindowMover
{
    /// <summary>Bit 0 of <c>+[NSEvent pressedMouseButtons]</c>.</summary>
    private const nint LeftButton = 1;

    private static readonly IntPtr NsEventClass = objc_getClass("NSEvent");
    private static readonly IntPtr PressedMouseButtonsSel = sel_registerName("pressedMouseButtons");
    private static readonly IntPtr NsApplicationClass = objc_getClass("NSApplication");
    private static readonly IntPtr SharedApplicationSel = sel_registerName("sharedApplication");
    private static readonly IntPtr CurrentEventSel = sel_registerName("currentEvent");
    private static readonly IntPtr PerformWindowDragSel = sel_registerName("performWindowDragWithEvent:");

    public async Task DragAsync(IPlatformHandle window, CancellationToken cancellationToken = default)
    {
        if (window is not IMacOSTopLevelPlatformHandle { NSWindow: not 0 } top)
            throw new InvalidOperationException("The window has no NSWindow to drag.");

        // The page reports the drag while the button is held, but the report travels: when the button was released on
        // the way there is no drag to start.
        if (!IsLeftButtonPressed())
            return;

        var application = objc_msgSend(NsApplicationClass, SharedApplicationSel);
        var current = objc_msgSend(application, CurrentEventSel);
        if (current == IntPtr.Zero)
            return;

        objc_msgSend(top.NSWindow, PerformWindowDragSel, current);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16));
        while (IsLeftButtonPressed())
            await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true);

        // Drain pending Cocoa windowDidMove notifications so Window.Position reflects the final drop point.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, cancellationToken);
    }

    private static bool IsLeftButtonPressed() =>
        ((nint)objc_msgSend(NsEventClass, PressedMouseButtonsSel) & LeftButton) != 0;
}
