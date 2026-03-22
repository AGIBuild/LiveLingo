using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

public class CoreOptionsSyncTests
{
    [Fact]
    public void AdvancedLlamaNativePathChanged_DetectsPathChange()
    {
        var a = Path.Combine(Path.GetTempPath(), "llama-native-a");
        var b = Path.Combine(Path.GetTempPath(), "llama-native-b");
        var before = new AdvancedSettings { LlamaNativeSearchPath = a };
        var after = new AdvancedSettings { LlamaNativeSearchPath = b };

        Assert.True(CoreOptionsSync.AdvancedLlamaNativePathChanged(before, after));
    }

    [Fact]
    public void AdvancedLlamaNativePathChanged_IgnoresWhitespaceOnlyChange()
    {
        var before = new AdvancedSettings { LlamaNativeSearchPath = null };
        var after = new AdvancedSettings { LlamaNativeSearchPath = "   " };

        Assert.False(CoreOptionsSync.AdvancedLlamaNativePathChanged(before, after));
    }
}
