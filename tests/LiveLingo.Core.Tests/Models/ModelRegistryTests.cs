using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public class ModelRegistryTests
{
    [Fact]
    public void TranslationModels_ContainsExpectedModels()
    {
        Assert.Equal(7, ModelRegistry.TranslationModels.Count);
        Assert.Contains(ModelRegistry.Gemma4_26B_A4B, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.Gemma4_E4B, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.Qwen25_7B, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianEnZh, ModelRegistry.TranslationModels);
        Assert.Contains(ModelRegistry.MarianJaEn, ModelRegistry.TranslationModels);
    }

    [Fact]
    public void CandidateTranslationModels_ContainsExpectedModels()
    {
        Assert.Equal(4, ModelRegistry.CandidateTranslationModels.Count);
        Assert.Contains(ModelRegistry.Gemma4_26B_A4B, ModelRegistry.CandidateTranslationModels);
        Assert.Contains(ModelRegistry.Gemma4_E4B, ModelRegistry.CandidateTranslationModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.CandidateTranslationModels);
        Assert.Contains(ModelRegistry.Qwen25_7B, ModelRegistry.CandidateTranslationModels);
        Assert.DoesNotContain(ModelRegistry.FastTextLid, ModelRegistry.CandidateTranslationModels);
        Assert.DoesNotContain(ModelRegistry.Qwen25_15B, ModelRegistry.CandidateTranslationModels);
    }

    [Fact]
    public void HasAnyTranslationModelInstalled_ReturnsFalse_WhenNoneInstalled()
    {
        var installed = Array.Empty<InstalledModel>();
        Assert.False(ModelRegistry.HasAnyTranslationModelInstalled(installed));
    }

    [Fact]
    public void HasAnyTranslationModelInstalled_ReturnsTrue_WhenAnyCandidateInstalled()
    {
        var installed = new[]
        {
            new InstalledModel(ModelRegistry.Gemma4_E4B.Id, ModelRegistry.Gemma4_E4B.DisplayName,
                "/p", ModelRegistry.Gemma4_E4B.SizeBytes, ModelType.Translation, DateTime.UtcNow)
        };
        Assert.True(ModelRegistry.HasAnyTranslationModelInstalled(installed));
    }

    [Fact]
    public void HasAnyTranslationModelInstalled_RespectsAssetCheck()
    {
        var installed = new[]
        {
            new InstalledModel(ModelRegistry.Gemma4_E4B.Id, ModelRegistry.Gemma4_E4B.DisplayName,
                "/p", ModelRegistry.Gemma4_E4B.SizeBytes, ModelType.Translation, DateTime.UtcNow)
        };
        Assert.False(ModelRegistry.HasAnyTranslationModelInstalled(installed, _ => false));
        Assert.True(ModelRegistry.HasAnyTranslationModelInstalled(installed, _ => true));
    }

    [Fact]
    public void Gemma4_26B_A4B_HasLoadFailureFallback_ToGemma4_E4B()
    {
        Assert.Same(ModelRegistry.Gemma4_E4B, ModelRegistry.Gemma4_26B_A4B.LoadFailureFallback);
    }

    [Fact]
    public void Gemma4_Models_HaveGemmaChatTemplate()
    {
        Assert.Equal(LocalModelChatTemplate.Gemma, ModelRegistry.Gemma4_26B_A4B.ChatTemplate);
        Assert.Equal(LocalModelChatTemplate.Gemma, ModelRegistry.Gemma4_E4B.ChatTemplate);
    }

    [Fact]
    public void QwenModels_HaveQwenChatTemplate()
    {
        Assert.Equal(LocalModelChatTemplate.Qwen, ModelRegistry.Qwen35_9B.ChatTemplate);
        Assert.Equal(LocalModelChatTemplate.Qwen, ModelRegistry.Qwen25_7B.ChatTemplate);
        Assert.Equal(LocalModelChatTemplate.Qwen, ModelRegistry.Qwen25_15B.ChatTemplate);
    }

    [Fact]
    public void OptionalModels_ContainsGemma4_E4B_And_Qwen()
    {
        Assert.Contains(ModelRegistry.Gemma4_E4B, ModelRegistry.OptionalModels);
        Assert.Contains(ModelRegistry.Qwen25_15B, ModelRegistry.OptionalModels);
    }

    [Fact]
    public void AllModels_ContainsAll()
    {
        Assert.Equal(12, ModelRegistry.AllModels.Count);
        Assert.Contains(ModelRegistry.Gemma4_26B_A4B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Gemma4_E4B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Qwen35_9B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Qwen25_7B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.Qwen25_15B, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.MarianZhEn, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.FastTextLid, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.SherpaCohereTranscribe14LangInt8, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.SherpaSenseVoiceSmallInt8, ModelRegistry.AllModels);
        Assert.Contains(ModelRegistry.SileroVad, ModelRegistry.AllModels);
    }

    [Fact]
    public void Qwen35_9B_HasLoadFailureFallback_ToQwen25()
    {
        Assert.Same(ModelRegistry.Qwen25_15B, ModelRegistry.Qwen35_9B.LoadFailureFallback);
    }

    [Fact]
    public void Qwen25_15B_HasCorrectType()
    {
        Assert.Equal(ModelType.PostProcessing, ModelRegistry.Qwen25_15B.Type);
        Assert.True(ModelRegistry.Qwen25_15B.SizeBytes > 0);
    }

    [Theory]
    [InlineData("zh", "en", "gemma4-26b-a4b")]
    [InlineData("en", "zh", "gemma4-26b-a4b")]
    [InlineData("ja", "en", "gemma4-26b-a4b")]
    public void FindTranslationModel_FindsCorrectModel(string src, string tgt, string expectedId)
    {
        var model = ModelRegistry.FindTranslationModel(src, tgt);
        Assert.NotNull(model);
        Assert.Equal(expectedId, model.Id);
    }

    [Fact]
    public void GetCandidateModelsForLanguagePair_ReturnsAllCandidates()
    {
        var candidates = ModelRegistry.GetCandidateModelsForLanguagePair("zh", "en");
        Assert.Equal(ModelRegistry.CandidateTranslationModels.Count, candidates.Count);
        Assert.Contains(ModelRegistry.Gemma4_26B_A4B, candidates);
        Assert.Contains(ModelRegistry.Gemma4_E4B, candidates);
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
            Assert.False(string.IsNullOrWhiteSpace(model.DownloadUrl));
    }

    [Fact]
    public void AllModels_HavePositiveSize()
    {
        foreach (var model in ModelRegistry.AllModels)
            Assert.True(model.SizeBytes > 0);
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
    public void SherpaCohereTranscribe_HasCorrectTypeAndArchiveLayout()
    {
        var model = ModelRegistry.SherpaCohereTranscribe14LangInt8;
        Assert.Equal(ModelType.SpeechToText, model.Type);
        Assert.Equal(ModelArchiveType.TarBz2, model.ArchiveType);
        Assert.Contains("encoder.int8.onnx", model.ExtractedFiles);
        Assert.Contains("decoder.int8.onnx", model.ExtractedFiles);
        Assert.Contains("tokens.txt", model.ExtractedFiles);
        Assert.True(model.SizeBytes > 0);
    }

    [Fact]
    public void SpeechToTextModels_ContainsCohereAndSenseVoice()
    {
        Assert.Equal(2, ModelRegistry.SpeechToTextModels.Count);
        Assert.Contains(ModelRegistry.SherpaCohereTranscribe14LangInt8, ModelRegistry.SpeechToTextModels);
        Assert.Contains(ModelRegistry.SherpaSenseVoiceSmallInt8, ModelRegistry.SpeechToTextModels);
        Assert.All(ModelRegistry.SpeechToTextModels, m => Assert.Equal(ModelType.SpeechToText, m.Type));
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
    [InlineData("gemma4-26b-a4b", "Gemma 4 26B-A4B MoE (GGUF Q4_K_M)")]
    [InlineData("gemma4-e4b", "Gemma 4 E4B (GGUF Q4_K_M)")]
    [InlineData("qwen25-1.5b", "Qwen2.5-1.5B-Instruct (GGUF Q4_K_M)")]
    [InlineData("qwen35-9b", "Qwen3.5-9B Abliterated (GGUF Q4_K_M)")]
    public void Model_HasExpectedIdAndDisplayName(string expectedId, string expectedName)
    {
        var model = ModelRegistry.AllModels.FirstOrDefault(m => m.Id == expectedId);
        Assert.NotNull(model);
        Assert.Equal(expectedName, model.DisplayName);
    }
}
