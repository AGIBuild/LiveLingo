using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelRegistryTests
{
    [Fact]
    public void TranslationModels_ContainsExpectedModels()
    {
        Assert.Equal(4, ModelRegistry.TranslationModels.Count);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianEnZh, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianJaEn, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.TranslationModels);
    }

    [Fact]
    public void RequiredModels_ContainsDefaultQwenPairOnly()
    {
        Assert.NotEmpty(ModelRegistry.RequiredModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.RequiredModels);
        Assert.DoesNotContain(ModelRegistry.FastTextLid, ModelRegistry.RequiredModels);
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
        Assert.Equal(8, ModelRegistry.AllModels.Count);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.FastTextLid, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Qwen25_15B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.WhisperBase, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.SileroVad, ModelRegistry.AllModels);
    }

    [Fact]
    public void Qwen25_15B_HasCorrectType()
    {
        Assert.Equal(ModelType.PostProcessing, ModelRegistry.Qwen25_15B.Type);
        Assert.True(ModelRegistry.Qwen25_15B.SizeBytes > 0);
    }

    [Theory]
    [InlineData("zh", "en", "qwen35-9b")]
    [InlineData("en", "zh", "qwen35-9b")]
    [InlineData("ja", "en", "qwen35-9b")]
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
        Assert.NotNull(ModelRegistry.FindTranslationModel(src, tgt));
    }

    [Fact]
    public void TranslationModels_UseMultiAssetOnnxLayout()
    {
        foreach (var model in ModelRegistry.TranslationModels.Where(m => m.Id.StartsWith("opus-mt-")))
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
    public void FastTextLid_HasExpectedSizeBytes()
    {
        Assert.Equal(938_013, ModelRegistry.FastTextLid.SizeBytes);
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

    [Fact]
    public void SileroVad_HasCorrectType()
    {
        Assert.Equal(ModelType.VoiceActivityDetection, ModelRegistry.SileroVad.Type);
        Assert.True(ModelRegistry.SileroVad.SizeBytes > 0);
    }

    [Fact]
    public void OptionalModels_ContainsSileroVad()
    {
        Assert.Contains(ModelRegistry.SileroVad, ModelRegistry.OptionalModels);
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
