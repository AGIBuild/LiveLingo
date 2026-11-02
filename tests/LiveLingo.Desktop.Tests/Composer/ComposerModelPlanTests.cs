using LiveLingo.Core.Models;
using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerModelPlanTests
{
    [Fact]
    public void Local_NeedsTheRuntimeAndTheModelOfTheOutput()
    {
        var plan = ComposerModelPlan.Create(TranslatorTier.Fast, cleanInCloud: false, translateInCloud: false);

        Assert.Equal([ModelCatalog.LlamaRuntime, ModelCatalog.Cleaner], plan.For(ComposerOutput.Clean));
        Assert.Equal([ModelCatalog.LlamaRuntime, ModelCatalog.TranslatorFast], plan.For(ComposerOutput.Translate));
        Assert.Empty(plan.For(ComposerOutput.Original));
    }

    [Fact]
    public void TheQualityTier_SelectsTheLargerTranslator()
    {
        var plan = ComposerModelPlan.Create(TranslatorTier.Quality, false, false);

        Assert.Contains(ModelCatalog.TranslatorQuality, plan.For(ComposerOutput.Translate));
    }

    [Fact]
    public void ARoleInTheCloud_NeedsNoLocalModels_ButTheOtherRoleStillDoes()
    {
        var plan = ComposerModelPlan.Create(TranslatorTier.Fast, cleanInCloud: false, translateInCloud: true);

        Assert.Empty(plan.For(ComposerOutput.Translate));
        Assert.Equal([ModelCatalog.LlamaRuntime, ModelCatalog.Cleaner], plan.For(ComposerOutput.Clean));
    }

    [Fact]
    public void Voice_AlwaysNeedsTheLocalSpeechModels_BecauseAudioNeverLeavesTheMachine()
    {
        var plan = ComposerModelPlan.Create(TranslatorTier.Fast, cleanInCloud: true, translateInCloud: true);

        Assert.Equal(ModelCatalog.Speech, plan.Voice);
        Assert.Empty(plan.For(ComposerOutput.Clean));
    }
}
