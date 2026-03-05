using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

internal static class TextInjector
{
    private static readonly string LogFile =
        Path.Combine(AppContext.BaseDirectory, "injection.log");

    private const uint WM_CHAR = 0x0102;
    private const int VK_RETURN_SCAN = 0x1C;

    public static void InjectText(IntPtr mainWindow, IntPtr inputChild, string text, bool autoSend)
    {
        Log($"=== Start main=0x{mainWindow:X}, child=0x{inputChild:X}, autoSend={autoSend}, len={text.Length} ===");

        ReleaseAllModifiers();
        Thread.Sleep(80);

        SetClipboardText(text);
        Thread.Sleep(50);

        ForceForeground(mainWindow);
        Thread.Sleep(500);

        var fg = GetForegroundWindow();
        Log($"Foreground: 0x{fg:X}, match={fg == mainWindow}");

        // ---- Strategy 1: SendInput Ctrl+V ----
        var r = SimulateKeyCombo(VK_CONTROL, VK_V);
        if (r > 0)
        {
            Log($"[S1:SendInput] Ctrl+V OK ({r} events)");
            if (autoSend)
            {
                Thread.Sleep(200);
                var rEnter = SimulateKey(VK_RETURN);
                Log($"[S1:SendInput] Enter={rEnter}");
            }
            Log($"=== Done (S1, autoSend={autoSend}) ===");
            return;
        }
        var err = Marshal.GetLastWin32Error();
        Log($"[S1:SendInput] FAILED err={err}");

        // ---- Strategy 2: WM_CHAR to Chrome renderer child ----
        var target = inputChild != IntPtr.Zero && inputChild != mainWindow
            ? inputChild
            : mainWindow;
        Log($"[S2:WM_CHAR] target=0x{target:X}");

        int sent = 0;
        foreach (var ch in text)
        {
            if (PostMessageW(target, WM_CHAR, (IntPtr)ch, IntPtr.Zero))
                sent++;
            Thread.Sleep(5);
        }
        Log($"[S2:WM_CHAR] sent {sent}/{text.Length} chars");

        if (autoSend)
        {
            Thread.Sleep(200);
            var lDown = MakeKeyLParam(1, VK_RETURN_SCAN, false, false);
            var lUp = MakeKeyLParam(1, VK_RETURN_SCAN, true, true);
            var r1 = PostMessageW(target, (uint)WM_KEYDOWN, (IntPtr)VK_RETURN, (IntPtr)lDown);
            Thread.Sleep(30);
            var r2 = PostMessageW(target, WM_KEYUP, (IntPtr)VK_RETURN, (IntPtr)lUp);
            Log($"[S2:PostMsg] Enter={r1},{r2}");
        }
        Log($"=== Done (S2, autoSend={autoSend}) ===");
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        AllowSetForegroundWindow(ASFW_ANY);
        ShowWindow(hwnd, SW_SHOW);
        BringWindowToTop(hwnd);

        var myThread = GetCurrentThreadId();
        var fgHwnd = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fgHwnd, out _);

        if (myThread != fgThread)
            AttachThreadInput(myThread, fgThread, true);

        SetForegroundWindow(hwnd);

        if (myThread != fgThread)
            AttachThreadInput(myThread, fgThread, false);
    }

    private static long MakeKeyLParam(int repeat, int scan, bool isUp, bool wasDown)
    {
        long lp = repeat & 0xFFFF;
        lp |= (long)(scan & 0xFF) << 16;
        if (wasDown) lp |= 1L << 30;
        if (isUp) lp |= 1L << 31;
        return lp;
    }

    private static void ReleaseAllModifiers()
    {
        foreach (var vk in new[] { VK_CONTROL, VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
            {
                var inp = new INPUT[1];
                inp[0] = MakeKeyInput((ushort)vk, KEYEVENTF_KEYUP);
                SendInput(1, inp, Marshal.SizeOf<INPUT>());
            }
        }
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
                    if (hGlobal == IntPtr.Zero) { Log("FAIL: GlobalAlloc=0"); return; }

                    var ptr = GlobalLock(hGlobal);
                    if (ptr == IntPtr.Zero) { Log("FAIL: GlobalLock=0"); return; }

                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr, text.Length * 2, 0);
                    GlobalUnlock(hGlobal);
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                    Log("Clipboard OK");
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(30);
        }

        Log("FAIL: clipboard open failed x10");
    }

    private static uint SimulateKeyCombo(int modifier, int key)
    {
        var inputs = new INPUT[4];
        inputs[0] = MakeKeyInput((ushort)modifier, KEYEVENTF_KEYDOWN);
        inputs[1] = MakeKeyInput((ushort)key, KEYEVENTF_KEYDOWN);
        inputs[2] = MakeKeyInput((ushort)key, KEYEVENTF_KEYUP);
        inputs[3] = MakeKeyInput((ushort)modifier, KEYEVENTF_KEYUP);
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
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = flags }
        }
    };

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
    }
}
