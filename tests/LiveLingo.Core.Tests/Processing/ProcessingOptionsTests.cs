using LiveLingo.Core.Processing;

namespace LiveLingo.Core.Tests.Processing;

public class ProcessingOptionsTests
{
    [Fact]
    public void Default_AllFalse()
    {
        var opts = new ProcessingOptions();
        Assert.False(opts.Summarize);
        Assert.False(opts.Optimize);
        Assert.False(opts.Colloquialize);
    }

    [Fact]
    public void CanEnable_Individual()
    {
        var opts = new ProcessingOptions(Summarize: true);
        Assert.True(opts.Summarize);
        Assert.False(opts.Optimize);
        Assert.False(opts.Colloquialize);
    }

    [Fact]
    public void CanEnable_All()
    {
        var opts = new ProcessingOptions(true, true, true);
        Assert.True(opts.Summarize);
        Assert.True(opts.Optimize);
        Assert.True(opts.Colloquialize);
    }

    [Fact]
    public void ProcessingMode_EnumValues()
    {
        Assert.Equal(0, (int)ProcessingMode.Off);
        Assert.Equal(1, (int)ProcessingMode.Summarize);
        Assert.Equal(2, (int)ProcessingMode.Optimize);
        Assert.Equal(3, (int)ProcessingMode.Colloquialize);
    }
}
