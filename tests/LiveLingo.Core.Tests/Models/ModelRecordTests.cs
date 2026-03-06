using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelRecordTests
{
    [Fact]
    public void ModelDescriptor_RecordEquality()
    {
        var a = new ModelDescriptor("m1", "Test Model", "http://example.com/m", 1000, ModelType.Translation);
        var b = new ModelDescriptor("m1", "Test Model", "http://example.com/m", 1000, ModelType.Translation);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ModelDescriptor_FieldAccess()
    {
        var d = new ModelDescriptor("m1", "MarianMT", "http://dl/m", 500_000_000, ModelType.Translation);
        Assert.Equal("m1", d.Id);
        Assert.Equal("MarianMT", d.DisplayName);
        Assert.Equal("http://dl/m", d.DownloadUrl);
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
    public void ModelType_EnumValues()
    {
        Assert.Equal(0, (int)ModelType.Translation);
        Assert.Equal(1, (int)ModelType.PostProcessing);
        Assert.Equal(2, (int)ModelType.LanguageDetection);
    }

    [Fact]
    public void ModelDescriptor_Inequality()
    {
        var a = new ModelDescriptor("m1", "A", "http://a", 100, ModelType.Translation);
        var b = new ModelDescriptor("m2", "B", "http://b", 200, ModelType.PostProcessing);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ModelDescriptor_ToString()
    {
        var d = new ModelDescriptor("m1", "Test", "http://x", 100, ModelType.Translation);
        Assert.Contains("m1", d.ToString());
    }

    [Fact]
    public void ModelDescriptor_GetHashCode()
    {
        var a = new ModelDescriptor("m1", "Test", "http://x", 100, ModelType.Translation);
        var b = new ModelDescriptor("m1", "Test", "http://x", 100, ModelType.Translation);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
