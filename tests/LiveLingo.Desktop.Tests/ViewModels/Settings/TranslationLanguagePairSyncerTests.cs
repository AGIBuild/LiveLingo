using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.ViewModels.Settings;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class TranslationLanguagePairSyncerTests
{
    private static readonly TranslationModelOption EnZh =
        new("opus-mt-en-zh", "EN→ZH", ModelType.Translation, "en", "zh", "en→zh");
    private static readonly TranslationModelOption ZhEn =
        new("opus-mt-zh-en", "ZH→EN", ModelType.Translation, "zh", "en", "zh→en");

    [Fact]
    public void SyncLanguagePairFromModel_WritesPair_WhenModelMatches()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings();

        sut.SyncLanguagePairFromModel(EnZh.Id, [EnZh, ZhEn], translation);

        Assert.Equal("en", translation.DefaultSourceLanguage);
        Assert.Equal("zh", translation.DefaultTargetLanguage);
    }

    [Fact]
    public void SyncLanguagePairFromModel_NoOp_WhenModelIdNotFound()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings { DefaultSourceLanguage = "fr", DefaultTargetLanguage = "de" };

        sut.SyncLanguagePairFromModel("ghost", [EnZh], translation);

        Assert.Equal("fr", translation.DefaultSourceLanguage);
        Assert.Equal("de", translation.DefaultTargetLanguage);
    }

    [Fact]
    public void SyncModelFromLanguagePair_WritesActiveId_WhenPairMatches()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings();

        sut.SyncModelFromLanguagePair("zh", "en", [EnZh, ZhEn], translation);

        Assert.Equal(ZhEn.Id, translation.ActiveTranslationModelId);
    }

    [Fact]
    public void SyncModelFromLanguagePair_ClearsActiveId_WhenNoPairMatches()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings { ActiveTranslationModelId = "stale" };

        sut.SyncModelFromLanguagePair("fr", "ja", [EnZh], translation);

        Assert.Null(translation.ActiveTranslationModelId);
    }

    [Fact]
    public void RestoreModelSelectionAfterRefresh_PrefersPreviousId()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings();

        sut.RestoreModelSelectionAfterRefresh(EnZh.Id, "zh", "en", [EnZh, ZhEn], translation);

        Assert.Equal(EnZh.Id, translation.ActiveTranslationModelId);
    }

    [Fact]
    public void RestoreModelSelectionAfterRefresh_FallsBackToLanguagePair_WhenIdMissing()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings();

        sut.RestoreModelSelectionAfterRefresh(previousModelId: null, "zh", "en", [EnZh, ZhEn], translation);

        Assert.Equal(ZhEn.Id, translation.ActiveTranslationModelId);
    }

    [Fact]
    public void IsSyncing_IsFalse_AfterEachSyncCompletes()
    {
        var sut = new TranslationLanguagePairSyncer();
        var translation = new TranslationSettings();

        sut.SyncLanguagePairFromModel(EnZh.Id, [EnZh], translation);
        sut.SyncModelFromLanguagePair("en", "zh", [EnZh], translation);
        sut.RestoreModelSelectionAfterRefresh(EnZh.Id, "en", "zh", [EnZh], translation);

        Assert.False(sut.IsSyncing);
    }
}
