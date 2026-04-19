using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels.Settings;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class CloudProviderPresetCoordinatorTests
{
    [Fact]
    public void ApplyPreset_RewritesBaseUrlAndModelPlaceholders_ForKnownPreset()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings { PresetId = "Groq" };

        sut.ApplyPreset(cloudProvider);

        Assert.Equal("https://api.groq.com/openai/v1", cloudProvider.BaseUrl);
        Assert.Equal("OpenAICompatible", cloudProvider.ProviderType);
        Assert.Equal("llama-3.3-70b-versatile", cloudProvider.TranslationModelId);
        Assert.Equal("llama-3.1-8b-instant", cloudProvider.PostProcessingModelId);
    }

    [Fact]
    public void ApplyPreset_DoesNotOverwriteCustomFields_ForCustomPreset()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings
        {
            PresetId = "Custom",
            BaseUrl = "https://my-gateway.example.com/v1",
            TranslationModelId = "my-model",
            PostProcessingModelId = "my-pp",
        };

        sut.ApplyPreset(cloudProvider);

        Assert.Equal("https://my-gateway.example.com/v1", cloudProvider.BaseUrl);
        Assert.Equal("my-model", cloudProvider.TranslationModelId);
        Assert.Equal("my-pp", cloudProvider.PostProcessingModelId);
    }

    [Fact]
    public void SyncPresetFromBaseUrl_InfersOpenRouterPreset()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings
        {
            PresetId = "Custom",
            BaseUrl = "https://openrouter.ai/api/v1"
        };

        sut.SyncPresetFromBaseUrl(cloudProvider);

        Assert.Equal("OpenRouter", cloudProvider.PresetId);
    }

    [Fact]
    public void SyncPresetFromBaseUrl_DoesNotEcho_WhenAlreadyMatching()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings
        {
            PresetId = "OpenAI",
            BaseUrl = "https://api.openai.com/v1"
        };

        sut.SyncPresetFromBaseUrl(cloudProvider);

        Assert.Equal("OpenAI", cloudProvider.PresetId);
        Assert.False(sut.IsRewritingPresetFields);
    }

    [Fact]
    public void GetSelectedPreset_ReturnsCustom_ForUnknownId()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings { PresetId = "DoesNotExist" };

        var preset = sut.GetSelectedPreset(cloudProvider);

        Assert.Equal("OpenAI", preset.Id);
    }

    [Fact]
    public void PresentationChanged_FiresAroundApplyPreset()
    {
        var sut = new CloudProviderPresetCoordinator();
        var cloudProvider = new CloudProviderSettings { PresetId = "OpenAI" };
        var fired = 0;
        sut.PresentationChanged += () => fired++;

        sut.ApplyPreset(cloudProvider);

        // Two PresentationChanged invocations: once before applying, once after.
        Assert.Equal(2, fired);
    }
}
