using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public class ModelReadinessServiceTests
{
    [Fact]
    public void EnsureTranslationModelReady_Throws_WhenModelMissing()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns([]);
        var service = new ModelReadinessService(manager);

        var ex = Assert.Throws<ModelNotReadyException>(() =>
            service.EnsureTranslationModelReady("zh", "en"));

        Assert.Equal(ModelType.Translation, ex.ModelType);
        Assert.Equal("qwen35-9b", ex.ModelId);
    }

    [Fact]
    public void EnsureTranslationModelReady_Throws_WhenPairUnknown()
    {
        var manager = Substitute.For<IModelManager>();
        var service = new ModelReadinessService(manager);

        var ex = Assert.Throws<ModelNotReadyException>(() =>
            service.EnsureTranslationModelReady("ko", "fr"));

        Assert.Equal("qwen35-9b", ex.ModelId);
    }

    [Fact]
    public void EnsurePostProcessingModelReady_DoesNotThrow_WhenInstalled()
    {
        var manager = Substitute.For<IModelManager>();
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
        var service = new ModelReadinessService(manager);

        service.EnsurePostProcessingModelReady();
    }
}
