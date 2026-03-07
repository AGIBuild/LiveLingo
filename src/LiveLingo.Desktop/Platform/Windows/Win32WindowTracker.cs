using System.Diagnostics;
using System.Runtime.InteropServices;
using static LiveLingo.Desktop.Platform.Windows.NativeMethods;

namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class Win32WindowTracker : IWindowTracker
{
    public TargetWindowInfo? GetForegroundWindowInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        var threadId = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        try
        {
            var process = Process.GetProcessById((int)pid);
            if (!GetWindowRect(hwnd, out var rect))
                return null;

            var inputChild = FindInputChild(hwnd, threadId);

            return new TargetWindowInfo(
                hwnd,
                inputChild,
                process.ProcessName,
                process.MainWindowTitle,
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);
        }
        catch
        {
            return null;
        }
    }

    private static nint FindInputChild(nint mainWindow, uint threadId)
    {
        var renderer = FindChromeRenderer(mainWindow);
        if (renderer != IntPtr.Zero)
            return renderer;

        var info = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
        };

        if (GetGUIThreadInfo(threadId, ref info)
            && info.hwndFocus != IntPtr.Zero
            && info.hwndFocus != mainWindow)
        {
            return info.hwndFocus;
        }

        return IntPtr.Zero;
    }

    private static nint FindChromeRenderer(nint parent)
    {
        var child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowExW(parent, child, null, null);
            if (child == IntPtr.Zero)
                break;

            var className = GetWindowClassName(child);
            if (className == "Chrome_RenderWidgetHostHWND")
                return child;

            var nested = FindChromeRenderer(child);
            if (nested != IntPtr.Zero)
                return nested;
        }
        return IntPtr.Zero;
    }

    private static string GetWindowClassName(nint hwnd)
    {
        var buf = new char[256];
        var len = GetClassNameW(hwnd, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : string.Empty;
    }
}
