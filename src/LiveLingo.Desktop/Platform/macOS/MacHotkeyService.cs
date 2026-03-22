using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Serilog;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacHotkeyService : IHotkeyService
{
    private readonly ConcurrentDictionary<string, HotkeyBinding> _bindings = new();
    private readonly CGEventTapCallBack _callback;
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;
    private IntPtr _runLoop;
    private Thread? _tapThread;
    private volatile bool _disposed;

    public event Action<HotkeyEventArgs>? HotkeyTriggered;

    public MacHotkeyService()
    {
        _callback = TapCallback;
    }

    public void Register(HotkeyBinding binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bindings[binding.Id] = binding;

        if (!AccessibilityPermission.IsGranted())
        {
            var granted = AccessibilityPermission.RequestAndCheck();
            if (!granted)
                throw new InvalidOperationException(
                    "CGEventTapCreate failed. Ensure Accessibility permission is granted.");
        }

        EnsureTapRunning();
    }

    public void Unregister(string hotkeyId)
    {
        _bindings.TryRemove(hotkeyId, out _);
    }

    private void EnsureTapRunning()
    {
        if (_eventTap != IntPtr.Zero) return;

        var eventMask = 1UL << (int)kCGEventKeyDown;
        _eventTap = CGEventTapCreate(
            kCGSessionEventTap,
            kCGHeadInsertEventTap,
            kCGEventTapOptionListenOnly,
            eventMask,
            _callback,
            IntPtr.Zero);

        if (_eventTap == IntPtr.Zero)
            throw new InvalidOperationException(
                "CGEventTapCreate failed. Ensure Accessibility permission is granted.");

        Log.Information("CGEventTap created successfully (handle=0x{Handle:X})", _eventTap);

        _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);

        _tapThread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "MacHotkeyService-CFRunLoop"
        };
        _tapThread.Start();
    }

    private void RunLoop()
    {
        _runLoop = CFRunLoopGetCurrent();
        CFRunLoopAddSource(_runLoop, _runLoopSource, kCFRunLoopCommonModes);
        CGEventTapEnable(_eventTap, true);
        Log.Information("CFRunLoop started, CGEventTap enabled — waiting for key events");
        CFRunLoopRun();
        Log.Information("CFRunLoop exited");
    }

    private IntPtr TapCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo)
    {
        if (type == kCGEventTapDisabledByTimeout)
        {
            Log.Information("CGEventTap was disabled by timeout, re-enabling");
            if (_eventTap != IntPtr.Zero)
                CGEventTapEnable(_eventTap, true);
            return @event;
        }

        if (type != kCGEventKeyDown)
            return @event;

        var keycode = (ushort)CGEventGetIntegerValueField(@event, kCGKeyboardEventKeycode);
        var flags = CGEventGetFlags(@event);
        var currentMods = CGEventFlagsToModifiers(flags);

        foreach (var (id, binding) in _bindings)
        {
            var expected = MapKeyToCGKeyCode(binding.Key);
            if (expected == 0xFFFF) continue;

            if (keycode == expected && currentMods == binding.Modifiers)
            {
                Log.Information("Hotkey triggered: id={Id}, mods={Mods}", id, currentMods);
                HotkeyTriggered?.Invoke(new HotkeyEventArgs(id));
                break;
            }
        }

        return @event;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(_runLoop);
        }

        // Wait for the run loop to actually exit before destroying the ports
        _tapThread?.Join(TimeSpan.FromSeconds(2));

        if (_eventTap != IntPtr.Zero)
        {
            CGEventTapEnable(_eventTap, false);
            CFMachPortInvalidate(_eventTap);
            CFRelease(_eventTap);
            _eventTap = IntPtr.Zero;
        }

        if (_runLoopSource != IntPtr.Zero)
        {
            CFRunLoopSourceInvalidate(_runLoopSource);
            CFRelease(_runLoopSource);
            _runLoopSource = IntPtr.Zero;
        }
    }
}
