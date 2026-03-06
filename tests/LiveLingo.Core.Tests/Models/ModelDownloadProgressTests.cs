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

    [Fact]
    public void RecordEquality()
    {
        var a = new ModelDownloadProgress("m1", 50, 200);
        var b = new ModelDownloadProgress("m1", 50, 200);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FieldAccess()
    {
        var p = new ModelDownloadProgress("model-id", 75, 150);
        Assert.Equal("model-id", p.ModelId);
        Assert.Equal(75, p.BytesDownloaded);
        Assert.Equal(150, p.TotalBytes);
    }

    [Fact]
    public void GetHashCode_SameForEqualRecords()
    {
        var a = new ModelDownloadProgress("m1", 50, 200);
        var b = new ModelDownloadProgress("m1", 50, 200);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_ContainsModelId()
    {
        var p = new ModelDownloadProgress("test-model", 50, 200);
        Assert.Contains("test-model", p.ToString());
    }
}
