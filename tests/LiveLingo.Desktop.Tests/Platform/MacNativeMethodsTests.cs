using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Platform.macOS;

namespace LiveLingo.Desktop.Tests.Platform;

public class MacNativeMethodsTests
{
    #region MapKeyToCGKeyCode

    [Theory]
    [InlineData("A", 0x00)]
    [InlineData("S", 0x01)]
    [InlineData("D", 0x02)]
    [InlineData("F", 0x03)]
    [InlineData("H", 0x04)]
    [InlineData("G", 0x05)]
    [InlineData("Z", 0x06)]
    [InlineData("X", 0x07)]
    [InlineData("C", 0x08)]
    [InlineData("V", 0x09)]
    [InlineData("B", 0x0B)]
    [InlineData("Q", 0x0C)]
    [InlineData("W", 0x0D)]
    [InlineData("E", 0x0E)]
    [InlineData("R", 0x0F)]
    [InlineData("Y", 0x10)]
    [InlineData("T", 0x11)]
    [InlineData("O", 0x1F)]
    [InlineData("U", 0x20)]
    [InlineData("I", 0x22)]
    [InlineData("P", 0x23)]
    [InlineData("L", 0x25)]
    [InlineData("J", 0x26)]
    [InlineData("K", 0x28)]
    [InlineData("N", 0x2D)]
    [InlineData("M", 0x2E)]
    public void MapKeyToCGKeyCode_Letters(string key, ushort expected)
    {
        Assert.Equal(expected, MacNativeMethods.MapKeyToCGKeyCode(key));
    }

    [Theory]
    [InlineData("0", 0x1D)]
    [InlineData("1", 0x12)]
    [InlineData("2", 0x13)]
    [InlineData("3", 0x14)]
    [InlineData("4", 0x15)]
    [InlineData("5", 0x17)]
    [InlineData("6", 0x16)]
    [InlineData("7", 0x1A)]
    [InlineData("8", 0x1C)]
    [InlineData("9", 0x19)]
    public void MapKeyToCGKeyCode_Digits(string key, ushort expected)
    {
        Assert.Equal(expected, MacNativeMethods.MapKeyToCGKeyCode(key));
    }

    [Theory]
    [InlineData("SPACE", 0x31)]
    [InlineData("RETURN", 0x24)]
    [InlineData("ENTER", 0x24)]
    [InlineData("TAB", 0x30)]
    [InlineData("ESCAPE", 0x35)]
    [InlineData("ESC", 0x35)]
    public void MapKeyToCGKeyCode_SpecialKeys(string key, ushort expected)
    {
        Assert.Equal(expected, MacNativeMethods.MapKeyToCGKeyCode(key));
    }

    [Theory]
    [InlineData("F1", 0x7A)]
    [InlineData("F2", 0x78)]
    [InlineData("F3", 0x63)]
    [InlineData("F4", 0x76)]
    [InlineData("F5", 0x60)]
    [InlineData("F6", 0x61)]
    [InlineData("F7", 0x62)]
    [InlineData("F8", 0x64)]
    [InlineData("F9", 0x65)]
    [InlineData("F10", 0x6D)]
    [InlineData("F11", 0x67)]
    [InlineData("F12", 0x6F)]
    public void MapKeyToCGKeyCode_FunctionKeys(string key, ushort expected)
    {
        Assert.Equal(expected, MacNativeMethods.MapKeyToCGKeyCode(key));
    }

    [Fact]
    public void MapKeyToCGKeyCode_CaseInsensitive()
    {
        Assert.Equal(MacNativeMethods.MapKeyToCGKeyCode("T"), MacNativeMethods.MapKeyToCGKeyCode("t"));
        Assert.Equal(MacNativeMethods.MapKeyToCGKeyCode("SPACE"), MacNativeMethods.MapKeyToCGKeyCode("space"));
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    [InlineData("F20")]
    public void MapKeyToCGKeyCode_UnknownReturns0xFFFF(string key)
    {
        Assert.Equal((ushort)0xFFFF, MacNativeMethods.MapKeyToCGKeyCode(key));
    }

    #endregion

    #region CGEventFlagsToModifiers

    [Fact]
    public void CGEventFlagsToModifiers_None()
    {
        Assert.Equal(KeyModifiers.None, MacNativeMethods.CGEventFlagsToModifiers(0));
    }

    [Fact]
    public void CGEventFlagsToModifiers_Control()
    {
        var mods = MacNativeMethods.CGEventFlagsToModifiers(MacNativeMethods.kCGEventFlagMaskControl);
        Assert.Equal(KeyModifiers.Ctrl, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_Alt()
    {
        var mods = MacNativeMethods.CGEventFlagsToModifiers(MacNativeMethods.kCGEventFlagMaskAlternate);
        Assert.Equal(KeyModifiers.Alt, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_Shift()
    {
        var mods = MacNativeMethods.CGEventFlagsToModifiers(MacNativeMethods.kCGEventFlagMaskShift);
        Assert.Equal(KeyModifiers.Shift, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_Command()
    {
        var mods = MacNativeMethods.CGEventFlagsToModifiers(MacNativeMethods.kCGEventFlagMaskCommand);
        Assert.Equal(KeyModifiers.Meta, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_CtrlAlt()
    {
        var flags = MacNativeMethods.kCGEventFlagMaskControl | MacNativeMethods.kCGEventFlagMaskAlternate;
        var mods = MacNativeMethods.CGEventFlagsToModifiers(flags);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_AllModifiers()
    {
        var flags = MacNativeMethods.kCGEventFlagMaskControl
                  | MacNativeMethods.kCGEventFlagMaskAlternate
                  | MacNativeMethods.kCGEventFlagMaskShift
                  | MacNativeMethods.kCGEventFlagMaskCommand;
        var mods = MacNativeMethods.CGEventFlagsToModifiers(flags);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta, mods);
    }

    [Fact]
    public void CGEventFlagsToModifiers_IgnoresUnrelatedBits()
    {
        // CapsLock (0x10000) and NumPad (0x200000) should be ignored
        ulong flags = 0x00010000 | 0x00200000 | MacNativeMethods.kCGEventFlagMaskControl;
        var mods = MacNativeMethods.CGEventFlagsToModifiers(flags);
        Assert.Equal(KeyModifiers.Ctrl, mods);
    }

    #endregion

    #region HotkeyParser integration with CGKeyCode

    [Theory]
    [InlineData("Ctrl+Alt+T", KeyModifiers.Ctrl | KeyModifiers.Alt, "T", 0x11)]
    [InlineData("Cmd+Space", KeyModifiers.Meta, "SPACE", 0x31)]
    [InlineData("Ctrl+Shift+L", KeyModifiers.Ctrl | KeyModifiers.Shift, "L", 0x25)]
    public void HotkeyParser_ProducesValidMacBinding(
        string input, KeyModifiers expectedMods, string expectedKey, ushort expectedKeyCode)
    {
        var binding = HotkeyParser.Parse("test", input);
        Assert.Equal(expectedMods, binding.Modifiers);
        Assert.Equal(expectedKey, binding.Key);
        Assert.Equal(expectedKeyCode, MacNativeMethods.MapKeyToCGKeyCode(binding.Key));
    }

    #endregion
}
