using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelDownloadProgressTests
{
    [Fact]
    public void Percentage_CalculatesCorrectly()
    {
        var p = new ModelDownloadProgress("m1", 50, 200);
        Assert.Equal(25.0, p.Percentage);
    }

    [Fact]
    public void Percentage_ReturnsZero_WhenTotalIsZero()
    {
        var p = new ModelDownloadProgress("m1", 0, 0);
        Assert.Equal(0.0, p.Percentage);
    }

    [Fact]
    public void Percentage_Returns100_WhenComplete()
    {
        var p = new ModelDownloadProgress("m1", 100, 100);
        Assert.Equal(100.0, p.Percentage);
    }
}
