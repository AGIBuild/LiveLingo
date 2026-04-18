using LiveLingo.Core.Models;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public class ModelReadinessServiceTests
{
    [Fact]
    public void EnsureTranslationModelReady_Throws_WhenModelMissing()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = CreateService(manager);

        var ex = Assert.Throws<ModelNotReadyException>(() =>
            service.EnsureTranslationModelReady("zh", "en"));

        Assert.Equal(ModelType.Translation, ex.ModelType);
        Assert.Equal("gemma4-26b-a4b", ex.ModelId);
    }

    [Fact]
    public void EnsureTranslationModelReady_Throws_WhenListedButAssetsIncomplete()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns(
        [
            new InstalledModel(
                ModelRegistry.Gemma4_26B_A4B.Id,
                ModelRegistry.Gemma4_26B_A4B.DisplayName,
                "/fake/qwen25_7b",
                ModelRegistry.Gemma4_26B_A4B.SizeBytes,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        manager.HasAllExpectedLocalAssets(ModelRegistry.Gemma4_26B_A4B).Returns(false);
        var service = CreateService(manager);

        Assert.Throws<ModelNotReadyException>(() => service.EnsureTranslationModelReady("zh", "en"));
    }

    [Fact]
    public void EnsureTranslationModelReady_Throws_WhenPairUnknown()
    {
        var manager = Substitute.For<IModelManager>();
        var service = CreateService(manager);

        var ex = Assert.Throws<ModelNotReadyException>(() =>
            service.EnsureTranslationModelReady("ko", "fr"));

        Assert.Equal("gemma4-26b-a4b", ex.ModelId);
    }

    [Fact]
    public void EnsureTranslationModelReady_ThrowsNotSupported_WhenNoChatProfileSupportsPair()
    {
        var manager = Substitute.For<IModelManager>();
        var service = CreateService(manager);

        var ex = Assert.Throws<NotSupportedException>(() =>
            service.EnsureTranslationModelReady("zh", "it"));

        Assert.Contains("zh→it", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsurePostProcessingModelReady_DoesNotThrow_WhenInstalled()
    {
        var manager = Substitute.For<IModelManager>();
        manager.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(true);
        manager.ListInstalled().Returns(
        [
            new InstalledModel(
                ModelRegistry.Qwen25_15B.Id,
                ModelRegistry.Qwen25_15B.DisplayName,
                "/fake/qwen",
                ModelRegistry.Qwen25_15B.SizeBytes,
                ModelType.PostProcessing,
                DateTime.UtcNow)
        ]);
        var service = CreateService(manager);

        service.EnsurePostProcessingModelReady();
    }

    [Fact]
    public void EnsurePostProcessingModelReady_DoesNotThrow_WhenGemma4_26B_A4BInstalled()
    {
        var manager = Substitute.For<IModelManager>();
        manager.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(true);
        manager.ListInstalled().Returns(
        [
            new InstalledModel(
                ModelRegistry.Gemma4_26B_A4B.Id,
                ModelRegistry.Gemma4_26B_A4B.DisplayName,
                "/fake/qwen25_7b",
                ModelRegistry.Gemma4_26B_A4B.SizeBytes,
                ModelType.Translation,
                DateTime.UtcNow)
        ]);
        var service = CreateService(manager);

        service.EnsurePostProcessingModelReady();
    }

    [Fact]
    public void EnsureTranslationModelReady_UsesConfiguredChatTranslationProfile()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = CreateService(manager, ModelRegistry.Qwen25_7B.Id);

        var ex = Assert.Throws<ModelNotReadyException>(() =>
            service.EnsureTranslationModelReady("zh", "en"));

        Assert.Equal(ModelRegistry.Qwen25_7B.Id, ex.ModelId);
    }

    [Fact]
    public void EnsurePostProcessingModelReady_FallsBackToDedicatedPostProcessingProfile_WhenActiveModelIsNotChatBased()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = CreateService(manager, ModelRegistry.MarianZhEn.Id);

        var ex = Assert.Throws<ModelNotReadyException>(() => service.EnsurePostProcessingModelReady());

        Assert.Equal(ModelRegistry.Qwen25_15B.Id, ex.ModelId);
    }

    [Fact]
    public void EnsureTranslationModelReady_DoesNotRequireLocalAssets_ForConfiguredCloudProfile()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = CreateService(
            manager,
            routingMode: TranslationRoutingMode.CloudOnly,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4.1-mini");

        service.EnsureTranslationModelReady("zh", "en");
    }

    [Fact]
    public void EnsurePostProcessingModelReady_DoesNotRequireLocalAssets_ForConfiguredCloudProfile()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = CreateService(
            manager,
            routingMode: TranslationRoutingMode.PreferCloud,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudPostProcessingModelId: "gpt-4.1-nano");

        service.EnsurePostProcessingModelReady();
    }

    private static ModelReadinessService CreateService(
        IModelManager manager,
        string? activeTranslationModelId = null,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routeUnsupportedPairsToCloud = false,
        bool routePostProcessingToCloud = false,
        bool cloudEnabled = false,
        string? cloudBaseUrl = null,
        string? cloudApiKey = null,
        string? cloudTranslationModelId = null,
        string? cloudPostProcessingModelId = null)
    {
        var selector = new DefaultModelSelector(
            new StaticModelCatalog(),
            Options.Create(new CoreOptions
            {
                ActiveTranslationModelId = activeTranslationModelId,
                TranslationRoutingMode = routingMode,
                RouteUnsupportedLanguagePairsToCloud = routeUnsupportedPairsToCloud,
                RoutePostProcessingToCloud = routePostProcessingToCloud,
                CloudProviderEnabled = cloudEnabled,
                CloudProviderBaseUrl = cloudBaseUrl,
                CloudProviderApiKey = cloudApiKey,
                CloudTranslationModelId = cloudTranslationModelId,
                CloudPostProcessingModelId = cloudPostProcessingModelId
            }));

        return new ModelReadinessService(manager, selector);
    }
}
