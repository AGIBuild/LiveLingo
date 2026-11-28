namespace LiveLingo.Core.Tests;

public class CoreOptionsTests
{
    [Fact]
    public void DefaultStoragePath_IsUnderLocalAppData()
    {
        var opts = new CoreOptions();

        Assert.Contains("LiveLingo", opts.ModelStoragePath);
        Assert.Contains("models", opts.ModelStoragePath);
    }

    [Fact]
    public void ModelsAndTranslatorTier_StaySynchronized()
    {
        var opts = new CoreOptions();
        Assert.Equal(LiveLingo.Core.Models.ModelSelection.Baseline, opts.Models);
        Assert.Equal(LiveLingo.Core.Models.TranslatorTier.Quality, opts.TranslatorTier);

        opts.TranslatorTier = LiveLingo.Core.Models.TranslatorTier.Fast;
        Assert.Equal(LiveLingo.Core.Models.ModelCatalog.TranslatorFast.Id, opts.Models.TranslatorModelId);
        Assert.Equal(LiveLingo.Core.Models.TranslatorTier.Fast, opts.TranslatorTier);

        opts.TranslatorTier = LiveLingo.Core.Models.TranslatorTier.Quality;
        Assert.Equal(LiveLingo.Core.Models.ModelCatalog.TranslatorQuality.Id, opts.Models.TranslatorModelId);
        Assert.Equal(LiveLingo.Core.Models.TranslatorTier.Quality, opts.TranslatorTier);

        opts.Models = new LiveLingo.Core.Models.ModelSelection(
            LiveLingo.Core.Models.ModelCatalog.SpeechFast.Id,
            LiveLingo.Core.Models.ModelCatalog.CleanerQuality.Id,
            LiveLingo.Core.Models.ModelCatalog.TranslatorFast.Id);
        Assert.Equal(LiveLingo.Core.Models.TranslatorTier.Fast, opts.TranslatorTier);
        Assert.Equal(LiveLingo.Core.Models.ModelCatalog.CleanerQuality, opts.Models.Cleaner);
        Assert.Equal(LiveLingo.Core.Models.ModelCatalog.SpeechFast, opts.Models.Speech);
    }
}
