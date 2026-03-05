using System;
using System.Runtime.InteropServices;
using static LiveLingo.App.Services.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Services.Platform.Windows;

/// <summary>
/// Low-level keyboard hook to detect global hotkey (Ctrl+Shift+L).
/// Uses WH_KEYBOARD_LL so it works regardless of which window has focus.
/// </summary>
internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event Action? HotkeyPressed;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        var moduleHandle = GetModuleHandleW(null);
        _hookId = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, moduleHandle, 0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Error: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (hookStruct.vkCode == VK_T
                && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
                && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
            {
                HotkeyPressed?.Invoke();
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
