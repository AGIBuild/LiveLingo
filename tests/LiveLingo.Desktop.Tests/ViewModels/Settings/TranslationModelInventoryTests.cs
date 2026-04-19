using LiveLingo.Core.Models;
using LiveLingo.Desktop.ViewModels.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels.Settings;

public sealed class TranslationModelInventoryTests
{
    private static readonly DateTime InstallTime = new(2026, 1, 1);

    [Fact]
    public void Snapshot_ReturnsEmpty_WhenModelManagerIsNull()
    {
        var localization = Substitute.For<ISettingsLocalizationHelper>();
        var sut = new TranslationModelInventory(modelManager: null, localization);

        var result = sut.Snapshot();

        Assert.Empty(result);
    }

    [Fact]
    public void Snapshot_FiltersToTranslationModelsOnly()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns(new List<InstalledModel>
        {
            Make("opus-mt-en-zh", ModelType.Translation),
            Make("qwen2.5-1.5b", ModelType.PostProcessing),
        });
        var sut = new TranslationModelInventory(manager, Substitute.For<ISettingsLocalizationHelper>());

        var result = sut.Snapshot();

        Assert.Single(result);
        Assert.Equal("opus-mt-en-zh", result[0].Id);
    }

    [Fact]
    public void Snapshot_DeduplicatesByModelId_CaseInsensitive()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns(new List<InstalledModel>
        {
            Make("opus-mt-en-zh", ModelType.Translation),
            Make("OPUS-MT-EN-ZH", ModelType.Translation),
        });
        var sut = new TranslationModelInventory(manager, Substitute.For<ISettingsLocalizationHelper>());

        var result = sut.Snapshot();

        Assert.Single(result);
    }

    [Fact]
    public void Snapshot_OrdersByDisplayName_CaseInsensitive()
    {
        var manager = Substitute.For<IModelManager>();
        manager.ListInstalled().Returns(new List<InstalledModel>
        {
            new("opus-mt-zh-en", "ZH→EN", "/p1", 1, ModelType.Translation, InstallTime),
            new("opus-mt-en-zh", "en→zh", "/p2", 1, ModelType.Translation, InstallTime),
        });
        var sut = new TranslationModelInventory(manager, Substitute.For<ISettingsLocalizationHelper>());

        var result = sut.Snapshot();

        Assert.Equal(2, result.Count);
        Assert.Equal("en→zh", result[0].DisplayName);
        Assert.Equal("ZH→EN", result[1].DisplayName);
    }

    [Theory]
    [InlineData("opus-mt-en-zh", true, "en", "zh")]
    [InlineData("opus-mt-zh-en", true, "zh", "en")]
    [InlineData("opus-mt-en", false, "", "")]
    [InlineData("not-opus-en-zh", false, "", "")]
    [InlineData("opus-mt-en-zh-extra", false, "", "")]
    public void TryParseLanguagePairFromModelId_ReturnsExpected(
        string modelId, bool expectedSuccess, string expectedSource, string expectedTarget)
    {
        var sut = new TranslationModelInventory(modelManager: null, Substitute.For<ISettingsLocalizationHelper>());

        var success = sut.TryParseLanguagePairFromModelId(modelId, out var source, out var target);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedSource, source);
        Assert.Equal(expectedTarget, target);
    }

    [Fact]
    public void BuildPairLabel_TranslationWithLanguages_ReturnsArrowFormat()
    {
        var sut = new TranslationModelInventory(modelManager: null, Substitute.For<ISettingsLocalizationHelper>());

        var label = sut.BuildPairLabel(ModelType.Translation, "en", "zh");

        Assert.Equal("en→zh", label);
    }

    [Fact]
    public void BuildPairLabel_PostProcessing_DelegatesToLocalization()
    {
        var localization = Substitute.For<ISettingsLocalizationHelper>();
        localization.Translate("settings.translation.pair.postProcessing", Arg.Any<string>())
            .Returns("Post-processing-ZH");
        var sut = new TranslationModelInventory(modelManager: null, localization);

        var label = sut.BuildPairLabel(ModelType.PostProcessing, null, null);

        Assert.Equal("Post-processing-ZH", label);
    }

    private static InstalledModel Make(string id, ModelType type) =>
        new(id, id, "/p", 1, type, InstallTime);
}
