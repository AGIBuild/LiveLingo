using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelRegistryTests
{
    [Fact]
    public void TranslationModels_ContainsExpectedModels()
    {
        Assert.Equal(3, ModelRegistry.TranslationModels.Count);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianEnZh, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianJaEn, ModelRegistry.TranslationModels);
    }

    [Fact]
    public void RequiredModels_ContainsFastTextAndDefaultMarianPair()
    {
        Assert.NotEmpty(ModelRegistry.RequiredModels);
        Assert.Contains(ModelRegistry.FastTextLid, ModelRegistry.RequiredModels);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.RequiredModels);
        Assert.DoesNotContain(ModelRegistry.Qwen25_15B, ModelRegistry.RequiredModels);
    }

    [Fact]
    public void OptionalModels_ContainsQwen()
    {
        Assert.Contains(ModelRegistry.Qwen25_15B, ModelRegistry.OptionalModels);
    }

    [Fact]
    public void AllModels_ContainsAll()
    {
        Assert.Equal(5, ModelRegistry.AllModels.Count);
        Assert.Contains(ModelRegistry.Qwen25_15B, ModelRegistry.AllModels);
    }

    [Fact]
    public void Qwen25_15B_HasCorrectType()
    {
        Assert.Equal(ModelType.PostProcessing, ModelRegistry.Qwen25_15B.Type);
        Assert.True(ModelRegistry.Qwen25_15B.SizeBytes > 0);
    }

    [Theory]
    [InlineData("zh", "en", "opus-mt-zh-en")]
    [InlineData("en", "zh", "opus-mt-en-zh")]
    [InlineData("ja", "en", "opus-mt-ja-en")]
    public void FindTranslationModel_FindsCorrectModel(string src, string tgt, string expectedId)
    {
        var model = ModelRegistry.FindTranslationModel(src, tgt);
        Assert.NotNull(model);
        Assert.Equal(expectedId, model.Id);
    }

    [Theory]
    [InlineData("ko", "en")]
    [InlineData("de", "fr")]
    public void FindTranslationModel_ReturnsNull_WhenNotFound(string src, string tgt)
    {
        Assert.Null(ModelRegistry.FindTranslationModel(src, tgt));
    }

    [Fact]
    public void TranslationModels_UseMultiAssetOnnxLayout()
    {
        foreach (var model in ModelRegistry.TranslationModels)
        {
            Assert.NotEmpty(model.Assets);
            Assert.Contains(model.Assets, a => a.RelativePath == "onnx/encoder_model.onnx");
            Assert.Contains(model.Assets, a => a.RelativePath == "onnx/decoder_model_merged.onnx");
            Assert.Contains(model.Assets, a => a.RelativePath == "source.spm");
            Assert.Contains(model.Assets, a => a.RelativePath == "target.spm");
        }
    }

    [Fact]
    public void MarianZhEn_HasCorrectType()
    {
        Assert.Equal(ModelType.Translation, ModelRegistry.MarianZhEn.Type);
    }

    [Fact]
    public void FastTextLid_HasCorrectType()
    {
        Assert.Equal(ModelType.LanguageDetection, ModelRegistry.FastTextLid.Type);
    }

    [Fact]
    public void AllModels_HaveNonEmptyUrls()
    {
        foreach (var model in ModelRegistry.AllModels)
        {
            Assert.False(string.IsNullOrWhiteSpace(model.DownloadUrl));
        }
    }

    [Fact]
    public void AllModels_HavePositiveSize()
    {
        foreach (var model in ModelRegistry.AllModels)
        {
            Assert.True(model.SizeBytes > 0);
        }
    }

    [Fact]
    public void AllModels_HaveNonEmptyIds()
    {
        foreach (var model in ModelRegistry.AllModels)
            Assert.False(string.IsNullOrEmpty(model.Id), $"Model {model.DisplayName} has empty ID");
    }

    [Fact]
    public void AllModels_HaveNonEmptyDisplayNames()
    {
        foreach (var model in ModelRegistry.AllModels)
            Assert.False(string.IsNullOrEmpty(model.DisplayName), $"Model {model.Id} has empty DisplayName");
    }

    [Theory]
    [InlineData("opus-mt-zh-en", "MarianMT Chinese\u2192English")]
    [InlineData("opus-mt-en-zh", "MarianMT English\u2192Chinese")]
    [InlineData("opus-mt-ja-en", "MarianMT Japanese\u2192English")]
    [InlineData("lid.176.ftz", "FastText Language Detection")]
    [InlineData("qwen25-1.5b", "Qwen2.5-1.5B-Instruct (GGUF Q4_K_M)")]
    public void Model_HasExpectedIdAndDisplayName(string expectedId, string expectedName)
    {
        var model = ModelRegistry.AllModels.FirstOrDefault(m => m.Id == expectedId);
        Assert.NotNull(model);
        Assert.Equal(expectedName, model.DisplayName);
    }
}
