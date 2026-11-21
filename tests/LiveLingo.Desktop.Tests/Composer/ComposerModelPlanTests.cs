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
    public void Voice_NeedsLocalSpeechModelsByDefault_AndNoneWhenSpeechRunsInCloud()
    {
        var localSpeech = ComposerModelPlan.Create(TranslatorTier.Fast, cleanInCloud: true, translateInCloud: true);
        Assert.Equal(ModelCatalog.Speech, localSpeech.Voice);
        Assert.Empty(localSpeech.For(ComposerOutput.Clean));

        var cloudSpeech = ComposerModelPlan.Create(
            ModelSelection.Baseline,
            cleanInCloud: false,
            translateInCloud: false,
            speechInCloud: true,
            speechNativeClean: true);
        Assert.Empty(cloudSpeech.Voice);
        Assert.Empty(cloudSpeech.Transcription);
        Assert.True(cloudSpeech.SpeechNativeClean);
    }

    [Fact]
    public void ModelSelection_WiresAllThreeStagesIntoThePlan()
    {
        var selection = new ModelSelection(
            ModelCatalog.SpeechFast.Id,
            ModelCatalog.CleanerQuality.Id,
            ModelCatalog.TranslatorQuality.Id);
        var plan = ComposerModelPlan.Create(selection, cleanInCloud: false, translateInCloud: false);

        Assert.Equal([ModelCatalog.LlamaRuntime, ModelCatalog.CleanerQuality], plan.For(ComposerOutput.Clean));
        Assert.Equal([ModelCatalog.LlamaRuntime, ModelCatalog.TranslatorQuality], plan.For(ComposerOutput.Translate));
        Assert.Equal(ModelCatalog.SpeechFor(ModelCatalog.SpeechFast), plan.Voice);
        Assert.Equal(ModelCatalog.TranscriptionFor(ModelCatalog.SpeechFast), plan.Transcription);
    }
}
