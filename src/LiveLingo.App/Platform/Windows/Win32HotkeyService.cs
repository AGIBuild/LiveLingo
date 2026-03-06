using System.Runtime.InteropServices;
using static LiveLingo.App.Platform.Windows.NativeMethods;

namespace LiveLingo.App.Platform.Windows;

internal sealed class Win32HotkeyService : IHotkeyService, IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly Dictionary<string, HotkeyBinding> _bindings = new();

    public event Action<HotkeyEventArgs>? HotkeyTriggered;

    public Win32HotkeyService()
    {
        _proc = HookCallback;
    }

    public void Register(HotkeyBinding binding)
    {
        _bindings[binding.Id] = binding;
        EnsureHookInstalled();
    }

    public void Unregister(string hotkeyId)
    {
        _bindings.Remove(hotkeyId);
    }

    private void EnsureHookInstalled()
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
            foreach (var (id, binding) in _bindings)
            {
                if (MatchesBinding(hookStruct, binding))
                {
                    HotkeyTriggered?.Invoke(new HotkeyEventArgs(id));
                    break;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool MatchesBinding(KBDLLHOOKSTRUCT hookStruct, HotkeyBinding binding)
    {
        var vk = MapKeyToVk(binding.Key);
        if (vk == 0 || hookStruct.vkCode != (uint)vk)
            return false;

        var ctrl = binding.Modifiers.HasFlag(Platform.KeyModifiers.Ctrl);
        var alt = binding.Modifiers.HasFlag(Platform.KeyModifiers.Alt);
        var shift = binding.Modifiers.HasFlag(Platform.KeyModifiers.Shift);

        var ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        var altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        var shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

        return ctrl == ctrlDown && alt == altDown && shift == shiftDown;
    }

    private static int MapKeyToVk(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterUpper(key[0]))
            return key[0];
        return key.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "RETURN" or "ENTER" => VK_RETURN,
            "ESCAPE" or "ESC" => 0x1B,
            "TAB" => 0x09,
            _ => key.Length == 1 ? char.ToUpper(key[0]) : 0,
        };
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
