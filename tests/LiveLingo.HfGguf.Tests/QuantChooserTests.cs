using LiveLingo.HfGguf;

namespace LiveLingo.HfGguf.Tests;

public class QuantChooserTests
{
    [Fact]
    public void Choose_PrefersQ4_K_M_ByDefault()
    {
        var paths = new[] { "a/Q2_K.gguf", "b/Q4_K_M.gguf", "c/Q4_K_S.gguf" };
        var pick = QuantChooser.Choose(paths, preferSaferMemory: false, contextLength: 4096, forcedQuant: null);
        Assert.Equal("b/Q4_K_M.gguf", pick);
    }

    [Fact]
    public void Choose_Ctx8192_UsesSaferOrder()
    {
        var paths = new[] { "x/Q4_K_M.gguf", "y/Q4_K_S.gguf" };
        var pick = QuantChooser.Choose(paths, preferSaferMemory: false, contextLength: 8192, forcedQuant: null);
        Assert.Equal("y/Q4_K_S.gguf", pick);
    }

    [Fact]
    public void Choose_ForcedQuant_MatchesSubstring()
    {
        var paths = new[] { "m/model-Q3_K_M-imatrix.gguf" };
        var pick = QuantChooser.Choose(paths, preferSaferMemory: false, contextLength: 4096, forcedQuant: "Q3_K_M");
        Assert.Equal("m/model-Q3_K_M-imatrix.gguf", pick);
    }

    [Fact]
    public void Choose_Empty_ReturnsNull()
    {
        Assert.Null(QuantChooser.Choose(Array.Empty<string>(), false, 4096, null));
    }
}
