using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal static class MacNativeMethods
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string AppSvc = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string ObjC = "/usr/lib/libobjc.dylib";

    #region CGEventTap

    public const int kCGSessionEventTap = 1;
    public const int kCGHeadInsertEventTap = 0;
    public const int kCGEventTapOptionListenOnly = 1;
    public const uint kCGEventKeyDown = 10;
    public const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;

    public const ulong kCGEventFlagMaskShift = 0x00020000;
    public const ulong kCGEventFlagMaskControl = 0x00040000;
    public const ulong kCGEventFlagMaskAlternate = 0x00080000;
    public const ulong kCGEventFlagMaskCommand = 0x00100000;

    public const int kCGKeyboardEventKeycode = 9;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr CGEventTapCallBack(
        IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo);

    [DllImport(CG)]
    public static extern IntPtr CGEventTapCreate(
        int tap, int place, int options,
        ulong eventsOfInterest,
        CGEventTapCallBack callback,
        IntPtr userInfo);

    [DllImport(CG)]
    public static extern void CGEventTapEnable(
        IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CG)]
    public static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport(CG)]
    public static extern long CGEventGetIntegerValueField(IntPtr @event, int field);

    #endregion

    #region CGEvent keyboard simulation

    [DllImport(CG)]
    public static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CG)]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(CG)]
    public static extern void CGEventPost(int tap, IntPtr @event);

    [DllImport(CG)]
    public static extern IntPtr CGEventCreate(IntPtr source);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint { public double X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize { public double Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect { public CGPoint Origin; public CGSize Size; }

    [DllImport(CG)]
    public static extern CGPoint CGEventGetLocation(IntPtr @event);

    #endregion

    #region CFRunLoop

    public static readonly IntPtr kCFRunLoopCommonModes;

    static MacNativeMethods()
    {
        var handle = NativeLibrary.Load(CF);
        kCFRunLoopCommonModes = Marshal.ReadIntPtr(
            NativeLibrary.GetExport(handle, "kCFRunLoopCommonModes"));
    }

    [DllImport(CF)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(
        IntPtr allocator, IntPtr port, int order);

    [DllImport(CF)]
    public static extern void CFRunLoopAddSource(
        IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CF)]
    public static extern void CFRunLoopRun();

    [DllImport(CF)]
    public static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport(CF)]
    public static extern void CFRunLoopWakeUp(IntPtr runLoop);

    [DllImport(CF)]
    public static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CF)]
    public static extern void CFRelease(IntPtr obj);

    [DllImport(CF)]
    public static extern void CFRunLoopSourceInvalidate(IntPtr source);

    [DllImport(CF)]
    public static extern void CFMachPortInvalidate(IntPtr port);

    #endregion

    #region Accessibility

    [DllImport(AppSvc)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(CF)]
    public static extern IntPtr CFDictionaryCreateMutable(
        IntPtr allocator, int capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CF)]
    public static extern void CFDictionarySetValue(IntPtr dict, IntPtr key, IntPtr value);

    public static IntPtr GetCFBooleanTrue()
    {
        var h = NativeLibrary.Load(CF);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(h, "kCFBooleanTrue"));
    }

    public static IntPtr GetAXTrustedCheckOptionPrompt()
    {
        var h = NativeLibrary.Load(AppSvc);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(h, "kAXTrustedCheckOptionPrompt"));
    }

    #endregion

    #region ObjC Runtime

    [DllImport(ObjC)]
    public static extern IntPtr objc_getClass(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    public static extern IntPtr sel_registerName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    #endregion

    #region CGWindowList

    public const int kCGWindowListOptionOnScreenOnly = 1;
    public const int kCGWindowListExcludeDesktopElements = 16;

    [DllImport(CG)]
    public static extern IntPtr CGWindowListCopyWindowInfo(int option, uint relativeToWindow);

    [DllImport(CF)]
    public static extern int CFArrayGetCount(IntPtr array);

    [DllImport(CF)]
    public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, int index);

    [DllImport(CF)]
    public static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [DllImport(CF)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFNumberGetValue(IntPtr number, int type, out int value);

    public const int kCFNumberSInt32Type = 3;

    [DllImport(CF)]
    public static extern IntPtr CFStringCreateWithCString(
        IntPtr alloc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str,
        uint encoding);

    [DllImport(CF)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFStringGetCString(
        IntPtr str, IntPtr buffer, int bufferSize, uint encoding);

    [DllImport(CF)]
    public static extern int CFStringGetLength(IntPtr str);

    public const uint kCFStringEncodingUTF8 = 0x08000100;

    [DllImport(CG)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRectMakeWithDictionaryRepresentation(
        IntPtr dict, out CGRect rect);

    #endregion

    #region Helpers

    public static IntPtr CreateCFString(string str) =>
        CFStringCreateWithCString(IntPtr.Zero, str, kCFStringEncodingUTF8);

    public static string? CFStringToString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;
        var length = CFStringGetLength(cfString);
        if (length == 0) return string.Empty;
        var bufferSize = length * 4 + 1;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            return CFStringGetCString(cfString, buffer, bufferSize, kCFStringEncodingUTF8)
                ? Marshal.PtrToStringUTF8(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static KeyModifiers CGEventFlagsToModifiers(ulong flags)
    {
        var mods = KeyModifiers.None;
        if ((flags & kCGEventFlagMaskControl) != 0) mods |= KeyModifiers.Ctrl;
        if ((flags & kCGEventFlagMaskAlternate) != 0) mods |= KeyModifiers.Alt;
        if ((flags & kCGEventFlagMaskShift) != 0) mods |= KeyModifiers.Shift;
        if ((flags & kCGEventFlagMaskCommand) != 0) mods |= KeyModifiers.Meta;
        return mods;
    }

    /// <summary>Maps a key name (e.g. "T", "SPACE") to macOS CGKeyCode.</summary>
    /// <returns>The CGKeyCode, or 0xFFFF if unmapped.</returns>
    public static ushort MapKeyToCGKeyCode(string key) => key.ToUpperInvariant() switch
    {
        "A" => 0x00, "S" => 0x01, "D" => 0x02, "F" => 0x03,
        "H" => 0x04, "G" => 0x05, "Z" => 0x06, "X" => 0x07,
        "C" => 0x08, "V" => 0x09, "B" => 0x0B, "Q" => 0x0C,
        "W" => 0x0D, "E" => 0x0E, "R" => 0x0F, "Y" => 0x10,
        "T" => 0x11, "1" => 0x12, "2" => 0x13, "3" => 0x14,
        "4" => 0x15, "6" => 0x16, "5" => 0x17, "9" => 0x19,
        "7" => 0x1A, "8" => 0x1C, "0" => 0x1D, "O" => 0x1F,
        "U" => 0x20, "I" => 0x22, "P" => 0x23, "L" => 0x25,
        "J" => 0x26, "K" => 0x28, "N" => 0x2D, "M" => 0x2E,
        "RETURN" or "ENTER" => 0x24,
        "TAB" => 0x30,
        "SPACE" => 0x31,
        "ESCAPE" or "ESC" => 0x35,
        "F1" => 0x7A, "F2" => 0x78, "F3" => 0x63, "F4" => 0x76,
        "F5" => 0x60, "F6" => 0x61, "F7" => 0x62, "F8" => 0x64,
        "F9" => 0x65, "F10" => 0x6D, "F11" => 0x67, "F12" => 0x6F,
        _ => 0xFFFF
    };

    public const ushort kVK_Command = 0x37;
    public const ushort kVK_V = 0x09;
    public const ushort kVK_Return = 0x24;

    #endregion
}
