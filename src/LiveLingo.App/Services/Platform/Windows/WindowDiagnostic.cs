using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

internal static class WindowDiagnostic
{
    public static void Run(string? processFilter = null)
    {
        IntPtr hwnd;
        if (!string.IsNullOrEmpty(processFilter))
        {
            Console.WriteLine($"[Diag] Looking for process: {processFilter}");
            hwnd = FindByProcess(processFilter);
        }
        else
        {
            Console.WriteLine("[Diag] Click on the target window within 5 seconds...");
            Thread.Sleep(5000);
            hwnd = GetForegroundWindow();
        }

        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("[Diag] No window found.");
            return;
        }

        var threadId = GetWindowThreadProcessId(hwnd, out var pid);
        string procName = "?";
        try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }

        Console.WriteLine($"[Diag] Window: hwnd=0x{hwnd:X}, pid={pid}, process={procName}");
        Console.WriteLine($"[Diag] ThreadId={threadId}");
        Console.WriteLine();

        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(threadId, ref info))
        {
            Console.WriteLine($"[GUITHREADINFO] hwndActive=0x{info.hwndActive:X}");
            Console.WriteLine($"[GUITHREADINFO] hwndFocus=0x{info.hwndFocus:X}");
        }
        else
        {
            Console.WriteLine("[GUITHREADINFO] FAILED (expected if not foreground)");
        }

        Console.WriteLine();
        Console.WriteLine("[ChildWindows] Enumerating (max depth 5):");
        int total = 0;
        DumpChildTree(hwnd, 0, 5, ref total);
        Console.WriteLine($"  Total child windows: {total}");

        Console.WriteLine();
        var renderer = FindChromeRenderer(hwnd);
        if (renderer != IntPtr.Zero)
        {
            Console.WriteLine($"[FindChromeRenderer] FOUND: 0x{renderer:X} class={GetWindowClassName(renderer)}");
        }
        else
        {
            Console.WriteLine("[FindChromeRenderer] NOT FOUND — this app may not be Electron/Chromium");
        }
    }

    private static IntPtr FindByProcess(string name)
    {
        var procs = Process.GetProcessesByName(name)
            .Where(p => p.MainWindowHandle != IntPtr.Zero)
            .ToArray();

        if (procs.Length == 0)
        {
            Console.WriteLine($"[Diag] No process named '{name}' with a visible window.");
            return IntPtr.Zero;
        }

        Console.WriteLine($"[Diag] Found {procs.Length} matching window(s)");
        return procs[0].MainWindowHandle;
    }

    private static void DumpChildTree(IntPtr parent, int depth, int maxDepth, ref int count)
    {
        if (depth >= maxDepth) return;

        var child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowExW(parent, child, null, null);
            if (child == IntPtr.Zero) break;

            count++;
            var cls = GetWindowClassName(child);
            var indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}  0x{child:X} [{cls}]");

            DumpChildTree(child, depth + 1, maxDepth, ref count);
        }
    }

    private static IntPtr FindChromeRenderer(IntPtr parent)
    {
        var child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowExW(parent, child, null, null);
            if (child == IntPtr.Zero) break;

            var className = GetWindowClassName(child);
            if (className == "Chrome_RenderWidgetHostHWND")
                return child;

            var nested = FindChromeRenderer(child);
            if (nested != IntPtr.Zero) return nested;
        }

        return IntPtr.Zero;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buf = new char[256];
        var len = GetClassNameW(hwnd, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : string.Empty;
    }
}
