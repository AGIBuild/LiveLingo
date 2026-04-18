using LiveLingo.Core.Models;
using LiveLingo.Core.Models.Installations;

namespace LiveLingo.Core.Tests.Models.Installations;

public sealed class ModelStoragePathsTests
{
    [Fact]
    public void GetModelDirectory_CombinesRootAndId()
    {
        var dir = ModelStoragePaths.GetModelDirectory("/root", "qwen25-1.5b");

        Assert.Equal(Path.Combine("/root", "qwen25-1.5b"), dir);
    }

    [Fact]
    public void GetManifestPath_AppendsManifestJson()
    {
        var path = ModelStoragePaths.GetManifestPath("/root/qwen");

        Assert.Equal(Path.Combine("/root/qwen", "manifest.json"), path);
    }

    [Fact]
    public void NormalizeRelativePath_ConvertsBackslashesToPlatformSeparator()
    {
        var normalised = ModelStoragePaths.NormalizeRelativePath("sub\\file.bin");

        Assert.Equal($"sub{Path.DirectorySeparatorChar}file.bin", normalised);
    }

    [Fact]
    public void NormalizeRelativePath_LeavesForwardSlashesAlone()
    {
        var normalised = ModelStoragePaths.NormalizeRelativePath("sub/file.bin");

        Assert.Equal("sub/file.bin", normalised);
    }

    [Fact]
    public void GetExpectedAssets_ReturnsDescriptorAssetsWhenPresent()
    {
        var asset = new ModelAsset("a.bin", "https://example.com/a.bin", 100);
        var descriptor = new ModelDescriptor("id", "Display", "https://example.com/a.bin", 100, ModelType.Translation)
        {
            Assets = [asset],
        };

        var assets = ModelStoragePaths.GetExpectedAssets(descriptor);

        Assert.Single(assets);
        Assert.Same(asset, assets[0]);
    }

    [Fact]
    public void GetExpectedAssets_SynthesisesSingletonAssetWhenDescriptorHasNone()
    {
        var descriptor = new ModelDescriptor("id", "Display", "https://example.com/path/model.gguf", 1234, ModelType.Translation);

        var assets = ModelStoragePaths.GetExpectedAssets(descriptor);

        Assert.Single(assets);
        Assert.Equal("model.gguf", assets[0].RelativePath);
        Assert.Equal("https://example.com/path/model.gguf", assets[0].DownloadUrl);
        Assert.Equal(1234, assets[0].SizeBytes);
    }

    [Fact]
    public void GetFileNameFromUrl_PicksLastSegment()
    {
        var name = ModelStoragePaths.GetFileNameFromUrl("https://huggingface.co/owner/repo/resolve/main/file.gguf");

        Assert.Equal("file.gguf", name);
    }

    [Fact]
    public void GetFileNameFromUrl_FallsBackToModelBinForRootPath()
    {
        var name = ModelStoragePaths.GetFileNameFromUrl("https://example.com/");

        Assert.Equal("model.bin", name);
    }
}
