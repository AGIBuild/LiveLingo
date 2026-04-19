using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.ViewModels.Settings;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class WorkingCopyNormalizerTests
{
    [Fact]
    public void Normalize_DefaultsRoutingMode_WhenMissing()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            ModelPolicy = new ModelPolicySettings { RoutingMode = "" }
        };

        sut.Normalize(translation);

        Assert.Equal(nameof(TranslationRoutingMode.PreferLocal), translation.ModelPolicy.RoutingMode);
    }

    [Fact]
    public void Normalize_DefaultsCloudProviderBaseUrl_WhenMissing()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            CloudProvider = new CloudProviderSettings { BaseUrl = "" }
        };

        sut.Normalize(translation);

        Assert.Equal("https://api.openai.com/v1", translation.CloudProvider.BaseUrl);
        Assert.Equal("OpenAICompatible", translation.CloudProvider.ProviderType);
    }

    [Fact]
    public void Normalize_InfersPresetIdFromKnownBaseUrl()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            CloudProvider = new CloudProviderSettings { BaseUrl = "https://openrouter.ai/api/v1" }
        };

        sut.Normalize(translation);

        Assert.Equal("OpenRouter", translation.CloudProvider.PresetId);
    }

    [Fact]
    public void Normalize_BackfillsPreferredLocalModelId_FromActiveId()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            ActiveTranslationModelId = "opus-mt-en-zh",
            ModelPolicy = new ModelPolicySettings { PreferredLocalTranslationModelId = "" }
        };

        sut.Normalize(translation);

        Assert.Equal("opus-mt-en-zh", translation.ModelPolicy.PreferredLocalTranslationModelId);
    }

    [Fact]
    public void Normalize_BackfillsActiveModelId_FromPreferredId_WhenActiveMissing()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            ActiveTranslationModelId = "",
            ModelPolicy = new ModelPolicySettings { PreferredLocalTranslationModelId = "opus-mt-zh-en" }
        };

        sut.Normalize(translation);

        Assert.Equal("opus-mt-zh-en", translation.ActiveTranslationModelId);
    }

    [Fact]
    public void ResolveInitialTranslationModel_PrefersExactIdMatch()
    {
        var sut = new WorkingCopyNormalizer();
        var available = new[]
        {
            MakeOption("opus-mt-en-zh", ModelType.Translation, "en", "zh"),
            MakeOption("opus-mt-zh-en", ModelType.Translation, "zh", "en"),
        };
        var translation = new TranslationSettings
        {
            ActiveTranslationModelId = "opus-mt-zh-en",
            DefaultSourceLanguage = "en",
            DefaultTargetLanguage = "zh",
        };

        var result = sut.ResolveInitialTranslationModel(translation, available);

        Assert.NotNull(result);
        Assert.Equal("opus-mt-zh-en", result!.Id);
    }

    [Fact]
    public void ResolveInitialTranslationModel_FallsBackToLanguagePair_WhenIdMissing()
    {
        var sut = new WorkingCopyNormalizer();
        var available = new[]
        {
            MakeOption("opus-mt-en-zh", ModelType.Translation, "en", "zh"),
        };
        var translation = new TranslationSettings
        {
            ActiveTranslationModelId = "",
            DefaultSourceLanguage = "en",
            DefaultTargetLanguage = "zh",
        };

        var result = sut.ResolveInitialTranslationModel(translation, available);

        Assert.NotNull(result);
        Assert.Equal("opus-mt-en-zh", result!.Id);
    }

    [Fact]
    public void ResolveInitialTranslationModel_ReturnsNull_WhenNoMatch()
    {
        var sut = new WorkingCopyNormalizer();
        var translation = new TranslationSettings
        {
            ActiveTranslationModelId = "non-existent",
            DefaultSourceLanguage = "fr",
            DefaultTargetLanguage = "de",
        };

        var result = sut.ResolveInitialTranslationModel(translation, []);

        Assert.Null(result);
    }

    private static TranslationModelOption MakeOption(string id, ModelType type, string? source, string? target) =>
        new(id, id, type, source, target, source is not null && target is not null ? $"{source}→{target}" : "");
}
