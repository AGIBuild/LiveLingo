using LiveLingo.Core.Models;
using LiveLingo.Core.Models.Installations;

namespace LiveLingo.Core.Tests.Models.Installations;

public sealed class ModelStoragePathsTests
{
    [Fact]
    public void GetModelDirectory_CombinesRootAndId()
    {
        Assert.Equal(Path.Combine("/root", "hy-mt2-1.8b"), ModelStoragePaths.GetModelDirectory("/root", "hy-mt2-1.8b"));
    }

    [Fact]
    public void GetManifestPath_AppendsManifestJson()
    {
        Assert.Equal(Path.Combine("/root/qwen", "manifest.json"), ModelStoragePaths.GetManifestPath("/root/qwen"));
    }

    [Fact]
    public void NormalizeRelativePath_ConvertsBackslashesToPlatformSeparator()
    {
        Assert.Equal($"sub{Path.DirectorySeparatorChar}file.bin", ModelStoragePaths.NormalizeRelativePath("sub\\file.bin"));
    }

    [Fact]
    public void NormalizeRelativePath_LeavesForwardSlashesAlone()
    {
        Assert.Equal("sub/file.bin", ModelStoragePaths.NormalizeRelativePath("sub/file.bin"));
    }

    [Fact]
    public void GetExpectedAssets_ReturnsTheDescriptorAssets()
    {
        var asset = TestModel.Asset("a.bin", "https://example.com/a.bin", 100);
        var descriptor = new ModelDescriptor("id", "Display", ModelType.Translation, "MIT", [asset]);

        Assert.Same(asset, Assert.Single(ModelStoragePaths.GetExpectedAssets(descriptor)));
    }

    [Fact]
    public void GetExpectedInstalledFiles_UsesAssetPaths_ForPlainDownloads()
    {
        var descriptor = new ModelDescriptor("id", "Display", ModelType.Translation, "MIT",
            [TestModel.Asset("a\\b.onnx", "https://example.com/a", 1), TestModel.Asset("c.spm", "https://example.com/c", 1)]);

        var files = ModelStoragePaths.GetExpectedInstalledFiles(descriptor);

        Assert.Equal([$"a{Path.DirectorySeparatorChar}b.onnx", "c.spm"], files);
    }

    [Fact]
    public void GetExpectedInstalledFiles_UsesExtractedFiles_ForArchives()
    {
        var descriptor = new ModelDescriptor("id", "Display", ModelType.Runtime, "MIT",
            [TestModel.Asset("bundle.tar.gz", "https://example.com/bundle.tar.gz", 1)])
        {
            ArchiveType = ModelArchiveType.TarGz,
            ExtractedFiles = ["llama-server"]
        };

        Assert.Equal(["llama-server"], ModelStoragePaths.GetExpectedInstalledFiles(descriptor));
    }

    [Fact]
    public void ModelDescriptor_SumsAssetSizes()
    {
        var descriptor = new ModelDescriptor("id", "Display", ModelType.Translation, "MIT",
            [TestModel.Asset("a", "https://example.com/a", 10), TestModel.Asset("b", "https://example.com/b", 32)]);

        Assert.Equal(42, descriptor.SizeBytes);
    }
}
