using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelRecordTests
{
    [Fact]
    public void ModelDescriptor_FieldAccess()
    {
        var d = TestModel.Create("m1", "MarianMT", "http://dl/m", 500_000_000, ModelType.Translation);
        Assert.Equal("m1", d.Id);
        Assert.Equal("MarianMT", d.DisplayName);
        Assert.Equal("http://dl/m", Assert.Single(d.Assets).DownloadUrl);
        Assert.Equal("test", d.License);
        Assert.Equal(500_000_000, d.SizeBytes);
        Assert.Equal(ModelType.Translation, d.Type);
    }

    [Fact]
    public void InstalledModel_RecordEquality()
    {
        var now = DateTime.UtcNow;
        var a = new InstalledModel("m1", "Test", "/p", 100, ModelType.PostProcessing, now);
        var b = new InstalledModel("m1", "Test", "/p", 100, ModelType.PostProcessing, now);
        Assert.Equal(a, b);
    }

    [Fact]
    public void InstalledModel_FieldAccess()
    {
        var now = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var m = new InstalledModel("qwen", "Qwen2.5", "/models/qwen", 2_000_000_000, ModelType.PostProcessing, now);
        Assert.Equal("qwen", m.Id);
        Assert.Equal("Qwen2.5", m.DisplayName);
        Assert.Equal("/models/qwen", m.LocalPath);
        Assert.Equal(2_000_000_000, m.SizeBytes);
        Assert.Equal(ModelType.PostProcessing, m.Type);
        Assert.Equal(now, m.InstalledAt);
    }

    [Fact]
    public void InstalledModel_Inequality()
    {
        var now = DateTime.UtcNow;
        var a = new InstalledModel("m1", "Model A", "/a", 100, ModelType.Translation, now);
        var b = new InstalledModel("m2", "Model B", "/b", 200, ModelType.PostProcessing, now);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void InstalledModel_ToString()
    {
        var now = DateTime.UtcNow;
        var m = new InstalledModel("m1", "Test", "/p", 100, ModelType.Translation, now);
        var str = m.ToString();
        Assert.Contains("m1", str);
        Assert.Contains("Test", str);
    }

    [Fact]
    public void InstalledModel_GetHashCode()
    {
        var now = DateTime.UtcNow;
        var a = new InstalledModel("m1", "Test", "/p", 100, ModelType.Translation, now);
        var b = new InstalledModel("m1", "Test", "/p", 100, ModelType.Translation, now);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ModelDescriptor_ToString()
    {
        var d = TestModel.Create("m1", "Test", "http://x", 100, ModelType.Translation);
        Assert.Contains("m1", d.ToString());
    }

}
