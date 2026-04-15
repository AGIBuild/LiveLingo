using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public sealed class StaticModelCatalogTests
{
    [Fact]
    public void AllProfiles_ContainsEveryRegisteredModel()
    {
        var catalog = new StaticModelCatalog();

        Assert.Equal(ModelRegistry.AllModels.Count, catalog.AllProfiles.Count);
    }

    [Fact]
    public void FindById_MapsQwenTranslationModel_ToChatExecutionProfile()
    {
        var catalog = new StaticModelCatalog();

        var profile = catalog.FindById(ModelRegistry.Qwen35_9B.Id);

        Assert.NotNull(profile);
        Assert.Equal(ModelTaskType.Translation, profile.TaskType);
        Assert.Equal(ModelProviderKind.LlamaServer, profile.ProviderKind);
        Assert.Equal(ModelRuntimeKind.LlamaServer, profile.RuntimeKind);
        Assert.Equal(ModelExecutionKind.ChatCompletions, profile.ExecutionKind);
        Assert.Contains("zh", profile.Languages);
        Assert.Contains("en", profile.Languages);
    }

    [Fact]
    public void FindById_MapsMarianModel_ToOnnxExecutionProfile()
    {
        var catalog = new StaticModelCatalog();

        var profile = catalog.FindById(ModelRegistry.MarianZhEn.Id);

        Assert.NotNull(profile);
        Assert.Equal(ModelTaskType.Translation, profile.TaskType);
        Assert.Equal(ModelProviderKind.MarianOnnx, profile.ProviderKind);
        Assert.Equal(ModelRuntimeKind.OnnxRuntime, profile.RuntimeKind);
        Assert.Equal(ModelExecutionKind.OnnxTranslation, profile.ExecutionKind);
        Assert.Equal(["zh", "en"], profile.Languages);
    }
}
