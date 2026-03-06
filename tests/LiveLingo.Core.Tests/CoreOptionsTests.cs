namespace LiveLingo.Core.Tests;

public class CoreOptionsTests
{
    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var opts = new CoreOptions();

        Assert.Equal("en", opts.DefaultTargetLanguage);
        Assert.Contains("LiveLingo", opts.ModelStoragePath);
        Assert.Contains("models", opts.ModelStoragePath);
    }

    [Fact]
    public void Properties_CanBeOverridden()
    {
        var opts = new CoreOptions
        {
            DefaultTargetLanguage = "zh",
            ModelStoragePath = "/custom/path"
        };

        Assert.Equal("zh", opts.DefaultTargetLanguage);
        Assert.Equal("/custom/path", opts.ModelStoragePath);
    }
}
