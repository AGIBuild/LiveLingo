using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelManifestTests
{
    [Fact]
    public void FromDescriptor_CopiesFields()
    {
        var desc = new ModelDescriptor("m1", "Test Model", "http://x", 1000, ModelType.Translation);
        var manifest = ModelManifest.FromDescriptor(desc);

        Assert.Equal("m1", manifest.Id);
        Assert.Equal("Test Model", manifest.DisplayName);
        Assert.Equal(1000, manifest.SizeBytes);
        Assert.Equal(ModelType.Translation, manifest.Type);
        Assert.True(manifest.DownloadedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ToJson_FromJson_Roundtrip()
    {
        var original = new ModelManifest(
            "test-model", "Test", 5000, ModelType.PostProcessing,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var json = original.ToJson();
        var restored = ModelManifest.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.DisplayName, restored.DisplayName);
        Assert.Equal(original.SizeBytes, restored.SizeBytes);
        Assert.Equal(original.Type, restored.Type);
    }

    [Fact]
    public void ToJson_ContainsExpectedFields()
    {
        var m = new ModelManifest("m1", "X", 100, ModelType.Translation, DateTime.UtcNow);
        var json = m.ToJson();

        Assert.Contains("\"id\"", json);
        Assert.Contains("\"displayName\"", json);
        Assert.Contains("\"sizeBytes\"", json);
    }

    [Fact]
    public void ToJson_IsIndented()
    {
        var m = new ModelManifest("m1", "X", 100, ModelType.Translation, DateTime.UtcNow);
        var json = m.ToJson();
        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    [Fact]
    public void FromJson_ReturnsNull_ForInvalidJson()
    {
        var result = ModelManifest.FromJson("not json");
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_ReturnsNull_ForMalformedJson()
    {
        var result = ModelManifest.FromJson("{invalid}");
        Assert.Null(result);
    }

    [Fact]
    public void FromJson_CatchesJsonException_DoesNotThrow()
    {
        var ex = Record.Exception(() => ModelManifest.FromJson("[broken"));
        Assert.Null(ex);
    }
}
