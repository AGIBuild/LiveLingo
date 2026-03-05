using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

public record TargetWindowInfo(
    IntPtr Handle,
    IntPtr InputChildHandle,
    string ProcessName,
    string Title,
    int Left, int Top, int Width, int Height);

internal static class WindowTracker
{
    public static TargetWindowInfo? GetForegroundWindowInfo()
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

    /// <summary>
    /// Finds the best child window to target for input injection.
    /// For Electron/Chromium apps: locates Chrome_RenderWidgetHostHWND.
    /// Fallback: uses GetGUIThreadInfo to find the focused child.
    /// </summary>
    private static IntPtr FindInputChild(IntPtr mainWindow, uint threadId)
    {
        // Strategy 1: Find Chromium renderer child (works for Slack, Teams, Discord, etc.)
        var renderer = FindChromeRenderer(mainWindow);
        if (renderer != IntPtr.Zero)
        {
            Log($"FindInputChild: Chrome renderer found={renderer}");
            return renderer;
        }

        // Strategy 2: GetGUIThreadInfo for the focused child
        var info = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
        };

        if (GetGUIThreadInfo(threadId, ref info)
            && info.hwndFocus != IntPtr.Zero
            && info.hwndFocus != mainWindow)
        {
            Log($"FindInputChild: GUITHREADINFO focus={info.hwndFocus}");
            return info.hwndFocus;
        }

        Log("FindInputChild: no child found, returning Zero");
        return IntPtr.Zero;
    }

    /// <summary>
    /// Recursively searches for Chrome_RenderWidgetHostHWND in the window tree.
    /// This is the actual input target in Electron/Chromium applications.
    /// </summary>
    private static IntPtr FindChromeRenderer(IntPtr parent)
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

            // Recurse into children (Electron nests widgets)
            var nested = FindChromeRenderer(child);
            if (nested != IntPtr.Zero)
                return nested;
        }

        return IntPtr.Zero;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buf = new char[256];
        var len = GetClassNameW(hwnd, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : string.Empty;
    }

    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "injection.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
    }
}
