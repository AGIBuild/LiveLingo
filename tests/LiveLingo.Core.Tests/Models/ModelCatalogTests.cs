using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelCatalogTests
{
    [Fact]
    public void EveryAsset_IsPinnedAndVerifiable()
    {
        foreach (var model in ModelCatalog.All)
        {
            Assert.NotEmpty(model.License);
            Assert.NotEmpty(model.Assets);
            foreach (var asset in model.Assets)
            {
                Assert.Matches("^[0-9a-f]{64}$", asset.Sha256);
                Assert.True(asset.SizeBytes > 0, asset.RelativePath);
                Assert.StartsWith("https://", asset.DownloadUrl);
                Assert.DoesNotContain("/main/", asset.DownloadUrl);
                Assert.DoesNotContain("/latest/", asset.DownloadUrl);
            }
        }
    }

    [Fact]
    public void HuggingFaceAssets_PinACommitHash()
    {
        var hosted = ModelCatalog.All.SelectMany(model => model.Assets)
            .Where(asset => asset.DownloadUrl.StartsWith("https://huggingface.co/", StringComparison.Ordinal));

        foreach (var asset in hosted)
            Assert.Matches("/resolve/[0-9a-f]{40}/", asset.DownloadUrl);
    }

    [Fact]
    public void ModelIds_AreUnique()
    {
        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(model => model.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("osx-arm64", ModelArchiveType.TarGz, "llama-server")]
    [InlineData("win-x64", ModelArchiveType.Zip, "llama-server.exe")]
    public void LlamaRuntime_IsSelectedByRuntimeIdentifier(string rid, ModelArchiveType archive, string executable)
    {
        var runtime = ModelCatalog.LlamaRuntimeFor(rid);

        Assert.Equal(ModelType.Runtime, runtime.Type);
        Assert.Equal(archive, runtime.ArchiveType);
        Assert.Equal([executable], runtime.ExtractedFiles);
        Assert.Contains("b11980", runtime.Assets[0].DownloadUrl);
    }

    [Fact]
    public void LlamaRuntime_RejectsUnsupportedPlatforms()
    {
        Assert.Throws<PlatformNotSupportedException>(() => ModelCatalog.LlamaRuntimeFor("linux-x64"));
    }

    [Fact]
    public void Translators_AreTranslationModels()
    {
        Assert.Equal(ModelType.Translation, ModelCatalog.TranslatorFast.Type);
        Assert.Equal(ModelType.Translation, ModelCatalog.TranslatorQuality.Type);
        Assert.True(ModelCatalog.TranslatorQuality.SizeBytes > ModelCatalog.TranslatorFast.SizeBytes);
    }
}
