using System.Runtime.InteropServices;
using static LiveLingo.Desktop.Platform.Windows.NativeMethods;

namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class Win32TextInjector : ITextInjector
{
    private const uint WM_CHAR = 0x0102;
    private const int VK_RETURN_SCAN = 0x1C;

    private readonly IClipboardService _clipboard;

    public Win32TextInjector(IClipboardService clipboard)
    {
        _clipboard = clipboard;
    }

    public Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        return Task.Run(() => InjectText(target.Handle, target.InputChildHandle, text, autoSend), ct);
    }

    private void InjectText(nint mainWindow, nint inputChild, string text, bool autoSend)
    {
        ReleaseAllModifiers();
        Thread.Sleep(80);

        Win32ClipboardService.SetClipboardText(text);
        Thread.Sleep(50);

        ForceForeground(mainWindow);
        Thread.Sleep(500);

        var r = SimulateKeyCombo(VK_CONTROL, VK_V);
        if (r > 0)
        {
            if (autoSend)
            {
                Thread.Sleep(200);
                SimulateKey(VK_RETURN);
            }
            return;
        }

        var target = inputChild != IntPtr.Zero && inputChild != mainWindow
            ? inputChild
            : mainWindow;

        foreach (var ch in text)
        {
            PostMessageW(target, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            Thread.Sleep(5);
        }

        if (autoSend)
        {
            Thread.Sleep(200);
            var lDown = MakeKeyLParam(1, VK_RETURN_SCAN, false, false);
            var lUp = MakeKeyLParam(1, VK_RETURN_SCAN, true, true);
            PostMessageW(target, (uint)WM_KEYDOWN, (IntPtr)VK_RETURN, (IntPtr)lDown);
            Thread.Sleep(30);
            PostMessageW(target, WM_KEYUP, (IntPtr)VK_RETURN, (IntPtr)lUp);
        }
    }

    private static void ForceForeground(nint hwnd)
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
}
