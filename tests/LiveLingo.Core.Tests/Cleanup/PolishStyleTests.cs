using LiveLingo.Core.Cleanup;

namespace LiveLingo.Core.Tests.Cleanup;

public class PolishStyleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bad")]
    [InlineData("Control\u0000characters are invalid.")]
    public void DamagedOrMissingRequirements_UseDefault(string? text) =>
        Assert.Equal(PolishStyle.Default, PolishStyle.Resolve(text));

    [Fact]
    public void TooLongRequirements_UseDefault() =>
        Assert.Equal(PolishStyle.Default, PolishStyle.Resolve(new string('x', PolishStyle.MaxLength + 1)));

    [Fact]
    public void CustomRequirements_AreNormalizedAndSaved()
    {
        const string custom = "  Keep a friendly tone.\r\nUse paragraphs.  ";
        Assert.Equal("Keep a friendly tone.\nUse paragraphs.", PolishStyle.Resolve(custom));
        Assert.Equal(PolishStyle.Resolve(custom), PolishStyle.ToStored(custom));
        Assert.Null(PolishStyle.ToStored("bad"));
        Assert.Equal("", PolishStyle.ToStored(PolishStyle.Default));
        Assert.Equal("", PolishStyle.ToStored("  "));
    }
}
