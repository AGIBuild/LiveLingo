namespace LiveLingo.Core.Tests;

public class CoreOptionsTests
{
    [Fact]
    public void DefaultStoragePath_IsUnderLocalAppData()
    {
        var opts = new CoreOptions();

        Assert.Contains("LiveLingo", opts.ModelStoragePath);
        Assert.Contains("models", opts.ModelStoragePath);
    }
}
