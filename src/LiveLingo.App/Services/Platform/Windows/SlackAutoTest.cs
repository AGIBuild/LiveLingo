using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

/// <summary>
/// Non-interactive test that tries all injection strategies against Slack
/// and logs detailed results. Does NOT send Enter to avoid sending actual messages.
/// </summary>
internal static class SlackAutoTest
{
    private const uint WM_CHAR = 0x0102;

    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "slack-test.log");

    public static void Run()
    {
        Log("=== Slack Auto-Test Start ===");

        var proc = Process.GetProcessesByName("Slack")
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (proc is null)
        {
            Log("FAIL: Slack not running or no visible window");
            Console.WriteLine("[SlackTest] Slack not found.");
            return;
        }

        var mainHwnd = proc.MainWindowHandle;
        Log($"Slack main: 0x{mainHwnd:X}, pid={proc.Id}");
        Console.WriteLine($"[SlackTest] Slack found: 0x{mainHwnd:X}");

        // Find Chrome renderer
        var renderer = FindChromeRenderer(mainHwnd);
        Log($"Chrome renderer: 0x{renderer:X} (found={renderer != IntPtr.Zero})");
        Console.WriteLine($"[SlackTest] Renderer: 0x{renderer:X}");

        if (renderer == IntPtr.Zero)
        {
            Log("FAIL: no Chrome_RenderWidgetHostHWND found");
            Console.WriteLine("[SlackTest] FAIL: renderer not found");
            return;
        }

        // Bring Slack to foreground first
        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(mainHwnd);
        Thread.Sleep(1000);

        var fg = GetForegroundWindow();
        Log($"Foreground after SetForeground: 0x{fg:X}, match={fg == mainHwnd}");
        Console.WriteLine($"[SlackTest] Foreground match: {fg == mainHwnd}");

        // ---- Test A: WM_CHAR to renderer (no Enter, just text) ----
        Log("--- Test A: WM_CHAR to renderer ---");
        Console.WriteLine("[SlackTest] Sending 'WM_CHAR_TEST ' via WM_CHAR...");
        var textA = "WM_CHAR_TEST ";
        int sentA = 0;
        foreach (var ch in textA)
        {
            if (PostMessageW(renderer, WM_CHAR, (IntPtr)ch, IntPtr.Zero))
                sentA++;
            Thread.Sleep(10);
        }
        Log($"WM_CHAR sent {sentA}/{textA.Length} to renderer 0x{renderer:X}");
        Console.WriteLine($"[SlackTest] WM_CHAR: {sentA}/{textA.Length} sent");
        Thread.Sleep(500);

        // ---- Test B: Clipboard + SendInput Ctrl+V ----
        Log("--- Test B: SendInput Ctrl+V ---");
        Console.WriteLine("[SlackTest] Setting clipboard and trying SendInput Ctrl+V...");
        SetClipboardText("SENDINPUT_TEST ");
        Thread.Sleep(100);

        // Re-verify foreground
        fg = GetForegroundWindow();
        Log($"Foreground before SendInput: 0x{fg:X}, match={fg == mainHwnd}");

        var inputs = new INPUT[4];
        inputs[0] = MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYDOWN);
        inputs[1] = MakeKeyInput(VK_V, KEYEVENTF_KEYDOWN);
        inputs[2] = MakeKeyInput(VK_V, KEYEVENTF_KEYUP);
        inputs[3] = MakeKeyInput(VK_CONTROL, KEYEVENTF_KEYUP);
        var r = SendInput(4, inputs, Marshal.SizeOf<INPUT>());
        var err = Marshal.GetLastWin32Error();
        Log($"SendInput returned {r}, err={err}");
        Console.WriteLine($"[SlackTest] SendInput Ctrl+V: returned {r}, err={err}");
        Thread.Sleep(500);

        // ---- Test C: WM_PASTE to renderer ----
        Log("--- Test C: WM_PASTE to renderer ---");
        SetClipboardText("WM_PASTE_TEST ");
        Thread.Sleep(100);
        var pasteResult = PostMessageW(renderer, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
        Log($"WM_PASTE to renderer: {pasteResult}");
        Console.WriteLine($"[SlackTest] WM_PASTE: {pasteResult}");

        Log("=== Slack Auto-Test Done ===");
        Console.WriteLine();
        Console.WriteLine("[SlackTest] Done! Check Slack's input box for:");
        Console.WriteLine("  - 'WM_CHAR_TEST '     → Test A worked (WM_CHAR)");
        Console.WriteLine("  - 'SENDINPUT_TEST '   → Test B worked (SendInput)");
        Console.WriteLine("  - 'WM_PASTE_TEST '    → Test C worked (WM_PASTE)");
        Console.WriteLine($"[SlackTest] Full log: {LogFile}");
    }

    private static IntPtr FindChromeRenderer(IntPtr parent)
    {
        var child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowExW(parent, child, null, null);
            if (child == IntPtr.Zero) break;

            var buf = new char[256];
            var len = GetClassNameW(child, buf, buf.Length);
            var cls = len > 0 ? new string(buf, 0, len) : "";

            if (cls == "Chrome_RenderWidgetHostHWND")
                return child;

            var nested = FindChromeRenderer(child);
            if (nested != IntPtr.Zero) return nested;
        }
        return IntPtr.Zero;
    }

    private static INPUT MakeKeyInput(int vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags } }
    };

    private static void SetClipboardText(string text)
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
                    var ptr = GlobalLock(hGlobal);
                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr, text.Length * 2, 0);
                    GlobalUnlock(hGlobal);
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                    return;
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(30);
        }
    }

    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        Console.WriteLine($"  [LOG] {msg}");
        try { File.AppendAllText(LogFile, line + "\n"); } catch { }
    }
}
