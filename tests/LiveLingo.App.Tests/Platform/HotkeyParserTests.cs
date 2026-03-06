using LiveLingo.App.Platform;

namespace LiveLingo.App.Tests.Platform;

public class HotkeyParserTests
{
    [Fact]
    public void Parse_CtrlAltT()
    {
        var binding = HotkeyParser.Parse("test", "Ctrl+Alt+T");
        Assert.Equal("test", binding.Id);
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Ctrl));
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Alt));
        Assert.False(binding.Modifiers.HasFlag(KeyModifiers.Shift));
        Assert.Equal("T", binding.Key);
    }

    [Fact]
    public void Parse_CtrlShiftL()
    {
        var binding = HotkeyParser.Parse("x", "Ctrl+Shift+L");
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Ctrl));
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Shift));
        Assert.Equal("L", binding.Key);
    }

    [Fact]
    public void Parse_CmdSpace()
    {
        var binding = HotkeyParser.Parse("mac", "Cmd+Space");
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Meta));
        Assert.Equal("SPACE", binding.Key);
    }

    [Fact]
    public void Parse_Win()
    {
        var binding = HotkeyParser.Parse("w", "Win+A");
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Meta));
        Assert.Equal("A", binding.Key);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var binding = HotkeyParser.Parse("ci", "ctrl+alt+t");
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Ctrl));
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Alt));
        Assert.Equal("T", binding.Key);
    }

    [Fact]
    public void Parse_WithSpaces()
    {
        var binding = HotkeyParser.Parse("sp", "Ctrl + Alt + T");
        Assert.True(binding.Modifiers.HasFlag(KeyModifiers.Ctrl));
        Assert.Equal("T", binding.Key);
    }

    [Fact]
    public void Parse_ThrowsOnNoKey()
    {
        Assert.Throws<ArgumentException>(() =>
            HotkeyParser.Parse("bad", "Ctrl+Alt"));
    }

    [Fact]
    public void Parse_SingleKey()
    {
        var binding = HotkeyParser.Parse("k", "F1");
        Assert.Equal(KeyModifiers.None, binding.Modifiers);
        Assert.Equal("F1", binding.Key);
    }

    [Theory]
    [InlineData("Control+A", KeyModifiers.Ctrl)]
    [InlineData("Option+B", KeyModifiers.Alt)]
    [InlineData("Command+C", KeyModifiers.Meta)]
    [InlineData("Super+D", KeyModifiers.Meta)]
    [InlineData("Meta+E", KeyModifiers.Meta)]
    public void Parse_AlternateModifierNames(string input, KeyModifiers expected)
    {
        var binding = HotkeyParser.Parse("alt", input);
        Assert.True(binding.Modifiers.HasFlag(expected));
    }
}
