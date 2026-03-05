using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

internal static class InjectionTest
{
    private const uint WM_CHAR = 0x0102;
    private const int VK_RETURN_SCAN = 0x1C;

    public static void Run()
    {
        Console.WriteLine("[Test] === LiveLingo Injection Test ===");
        Console.WriteLine($"[Test] sizeof(INPUT)={Marshal.SizeOf<INPUT>()}, expected=40 on x64");
        Console.WriteLine();

        Console.Write("[Test] Target app (1=Notepad, 2=Slack, 3=Foreground): ");
        var choice = Console.ReadLine()?.Trim() ?? "3";

        IntPtr mainWindow;
        IntPtr rendererChild;

        switch (choice)
        {
            case "1":
                (mainWindow, rendererChild) = FindNotepad();
                break;
            case "2":
                (mainWindow, rendererChild) = FindSlack();
                break;
            default:
                Console.WriteLine("[Test] Click on target window in 3 seconds...");
                Thread.Sleep(3000);
                mainWindow = GetForegroundWindow();
                rendererChild = FindChromeRenderer(mainWindow);
                break;
        }

        if (mainWindow == IntPtr.Zero)
        {
            Console.WriteLine("[Test] FAIL: no window found");
            return;
        }

        Console.WriteLine($"[Test] mainWindow=0x{mainWindow:X}");
        Console.WriteLine($"[Test] rendererChild=0x{rendererChild:X} (distinct={rendererChild != IntPtr.Zero && rendererChild != mainWindow})");
        Console.WriteLine();

        var target = rendererChild != IntPtr.Zero ? rendererChild : mainWindow;

        // Test WM_CHAR directly (no foreground needed)
        Console.WriteLine("[Test] --- Test 1: WM_CHAR direct ---");
        var testText = "Hello from LiveLingo!";
        int sent = 0;
        foreach (var ch in testText)
        {
            if (PostMessageW(target, WM_CHAR, (IntPtr)ch, IntPtr.Zero))
                sent++;
            Thread.Sleep(5);
        }
        Console.WriteLine($"[Test] WM_CHAR sent {sent}/{testText.Length} chars to 0x{target:X}");
        Console.WriteLine("[Test] Check if text appeared in the target input.");
        Console.WriteLine();

        Console.Write("[Test] Did it work? (y/n): ");
        var worked = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";

        if (worked)
        {
            Console.WriteLine("[Test] WM_CHAR works! Testing Enter...");
            Thread.Sleep(500);

            // Test Enter key via PostMessage
            var lDown = MakeKeyLParam(1, VK_RETURN_SCAN, false, false);
            var lUp = MakeKeyLParam(1, VK_RETURN_SCAN, true, true);
            PostMessageW(target, (uint)WM_KEYDOWN, (IntPtr)VK_RETURN, (IntPtr)lDown);
            Thread.Sleep(30);
            PostMessageW(target, WM_KEYUP, (IntPtr)VK_RETURN, (IntPtr)lUp);
            Console.WriteLine("[Test] Enter sent via PostMessage.");
        }
        else
        {
            Console.WriteLine("[Test] --- Test 2: Clipboard + SendInput Ctrl+V ---");
            SetForegroundWindow(mainWindow);
            Thread.Sleep(500);

            SetClipboardText(testText);
            Thread.Sleep(50);

            var r = SimulateKeyCombo(VK_CONTROL, VK_V);
            Console.WriteLine($"[Test] SendInput Ctrl+V: returned {r}, err={Marshal.GetLastWin32Error()}");

            Console.Write("[Test] Did paste work? (y/n): ");
            var pasteWorked = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";

            if (pasteWorked)
            {
                Console.WriteLine("[Test] SendInput works! Sending Enter...");
                Thread.Sleep(300);
                var rEnter = SimulateKey(VK_RETURN);
                Console.WriteLine($"[Test] SendInput Enter: returned {rEnter}");
            }
            else
            {
                Console.WriteLine("[Test] --- Test 3: WM_PASTE to renderer ---");
                PostMessageW(target, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                Console.WriteLine($"[Test] WM_PASTE sent to 0x{target:X}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("[Test] === Done ===");
    }

    private static (IntPtr main, IntPtr renderer) FindNotepad()
    {
        var procs = Process.GetProcessesByName("Notepad");
        if (procs.Length == 0)
        {
            Console.WriteLine("[Test] Starting Notepad...");
            Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
            Thread.Sleep(2000);
            procs = Process.GetProcessesByName("Notepad");
        }

        var proc = procs.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (proc == null) return (IntPtr.Zero, IntPtr.Zero);

        var hwnd = proc.MainWindowHandle;
        var threadId = GetWindowThreadProcessId(hwnd, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        var child = IntPtr.Zero;
        if (GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            child = info.hwndFocus;

        return (hwnd, child);
    }

    private static (IntPtr main, IntPtr renderer) FindSlack()
    {
        var proc = Process.GetProcessesByName("Slack")
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (proc == null)
        {
            Console.WriteLine("[Test] Slack not found or no visible window.");
            return (IntPtr.Zero, IntPtr.Zero);
        }

        var hwnd = proc.MainWindowHandle;
        var renderer = FindChromeRenderer(hwnd);
        return (hwnd, renderer);
    }

    private static IntPtr FindChromeRenderer(IntPtr parent)
    {
        var child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowExW(parent, child, null, null);
            if (child == IntPtr.Zero) break;

            var cls = GetWindowClassName(child);
            if (cls == "Chrome_RenderWidgetHostHWND")
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

    private static long MakeKeyLParam(int repeat, int scan, bool isUp, bool wasDown)
    {
        long lp = repeat & 0xFFFF;
        lp |= (long)(scan & 0xFF) << 16;
        if (wasDown) lp |= 1L << 30;
        if (isUp) lp |= 1L << 31;
        return lp;
    }

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

    private static uint SimulateKeyCombo(int mod, int key)
    {
        var inputs = new INPUT[4];
        inputs[0] = MakeKeyInput((ushort)mod, KEYEVENTF_KEYDOWN);
        inputs[1] = MakeKeyInput((ushort)key, KEYEVENTF_KEYDOWN);
        inputs[2] = MakeKeyInput((ushort)key, KEYEVENTF_KEYUP);
        inputs[3] = MakeKeyInput((ushort)mod, KEYEVENTF_KEYUP);
        return SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    private static uint SimulateKey(int key)
    {
        var inputs = new INPUT[2];
        inputs[0] = MakeKeyInput((ushort)key, KEYEVENTF_KEYDOWN);
        inputs[1] = MakeKeyInput((ushort)key, KEYEVENTF_KEYUP);
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };
}
