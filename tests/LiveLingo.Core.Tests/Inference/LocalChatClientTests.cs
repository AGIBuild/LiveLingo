using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Core.Tests.Inference;

public class LocalChatClientTests
{
    [Fact]
    public void Translator_FollowsTheConfiguredTier()
    {
        Assert.Same(ModelCatalog.TranslatorFast, ModelCatalog.Translator(TranslatorTier.Fast));
        Assert.Same(ModelCatalog.TranslatorQuality, ModelCatalog.Translator(TranslatorTier.Quality));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelCatalog.Translator((TranslatorTier)9));
    }

    [Fact]
    public async Task CompleteAsync_StartsTheServerThroughTheHost_AndReleasesTheLease()
    {
        var models = Substitute.For<IModelManager>();
        models.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(false);
        await using var host = new LlamaServerHost(
            models,
            ModelCatalog.LlamaRuntimeFor("osx-arm64"),
            Substitute.For<ILlamaProcessLauncher>(),
            Substitute.For<IServerHealthProbe>(),
            Substitute.For<IPortAllocator>(),
            new CoreOptions(),
            LlamaServerHostOptions.Default,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlamaServerHost>.Instance);
        var client = new LocalChatClient(host, InferenceRole.Translator, () => ModelCatalog.TranslatorFast);

        var ex = await Assert.ThrowsAsync<ModelNotReadyException>(() => client.CompleteAsync(
            new ChatRequest("p", 8, 0, new Dictionary<string, object>()), CancellationToken.None));

        Assert.Equal(ModelCatalog.LlamaRuntimeFor("osx-arm64").Id, ex.ModelId);
    }
}
