using System.Runtime.Versioning;
using Avalonia.Platform;

namespace LiveLingo.Desktop.Platform.Windows;

/// <summary>
/// Hands the held mouse button to the window's caption: the system then runs its own move loop (the same call
/// Avalonia's <c>BeginMoveDrag</c> makes, which cannot be used here because the web view, not Avalonia, got the click).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Win32WindowMover : IWindowMover
{
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int VK_LBUTTON = 0x01;

    public void BeginDrag(IPlatformHandle window)
    {
        if (window.Handle == IntPtr.Zero)
            throw new InvalidOperationException("The window has no handle to drag.");

        // The page reports the drag while the button is held, but the report travels: when the button was released on
        // the way there is no drag to start (the system's move loop would wait for a release that already happened).
        if ((NativeMethods.GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
            return;

        // The web view (a child window) holds the mouse capture; release it so the caption can take over.
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessageW(window.Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }
}
