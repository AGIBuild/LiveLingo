using System.Runtime.InteropServices;
using static LiveLingo.Desktop.Platform.Windows.NativeMethods;

namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class Win32TextInjector : ITextInjector
{
    private const uint WM_CHAR = 0x0102;
    private const int VK_RETURN_SCAN = 0x1C;

    private readonly IClipboardService _clipboard;
    private readonly IWindowTracker _windows;

    public Win32TextInjector(IClipboardService clipboard, IWindowTracker windows)
    {
        _clipboard = clipboard;
        _windows = windows;
    }

    public Task InjectAsync(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        return Task.Run(() => InjectText(target, text, autoSend, ct), ct);
    }

    private void InjectText(TargetWindowInfo target, string text, bool autoSend, CancellationToken ct)
    {
        var mainWindow = target.Handle;
        var inputChild = target.InputChildHandle;
        ct.ThrowIfCancellationRequested();
        ReleaseAllModifiers();
        WaitWithCancellation(80, ct);

        ct.ThrowIfCancellationRequested();
        Win32ClipboardService.SetClipboardText(text);
        WaitWithCancellation(50, ct);

        ct.ThrowIfCancellationRequested();
        _windows.Activate(target);
        WaitWithCancellation(500, ct);

        ct.ThrowIfCancellationRequested();
        var r = SimulateKeyCombo(VK_CONTROL, VK_V);
        if (r > 0)
        {
            if (autoSend)
            {
                WaitWithCancellation(200, ct);
                SimulateKey(VK_RETURN);
            }
            return;
        }

        var receiver = inputChild != IntPtr.Zero && inputChild != mainWindow
            ? inputChild
            : mainWindow;

        foreach (var ch in text)
        {
            ct.ThrowIfCancellationRequested();
            PostMessageW(receiver, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            WaitWithCancellation(5, ct);
        }

        if (autoSend)
        {
            WaitWithCancellation(200, ct);
            var lDown = MakeKeyLParam(1, VK_RETURN_SCAN, false, false);
            var lUp = MakeKeyLParam(1, VK_RETURN_SCAN, true, true);
            PostMessageW(receiver, (uint)WM_KEYDOWN, (IntPtr)VK_RETURN, (IntPtr)lDown);
            WaitWithCancellation(30, ct);
            PostMessageW(receiver, WM_KEYUP, (IntPtr)VK_RETURN, (IntPtr)lUp);
        }
    }

    private static void WaitWithCancellation(int milliseconds, CancellationToken ct)
    {
        const int slice = 20;
        var remaining = milliseconds;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = Math.Min(slice, remaining);
            Thread.Sleep(current);
            remaining -= current;
        }
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
