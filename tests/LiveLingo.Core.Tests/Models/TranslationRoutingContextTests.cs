using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public sealed class TranslationRoutingContextTests
{
    [Theory]
    [InlineData("zh", "en", false)]
    [InlineData("en", "zh", false)]
    [InlineData("ja", "en", false)]
    [InlineData("ko", "zh", false)]
    [InlineData("fr", "en", true)]   // French is not in common set
    [InlineData("zh", "de", true)]   // German is not in common set
    public void FromText_IsRareLanguagePair_DetectsCorrectly(
        string src, string tgt, bool expectedRare)
    {
        var ctx = TranslationRoutingContext.FromText("hello", src, tgt);
        Assert.Equal(expectedRare, ctx.IsRareLanguagePair);
    }

    [Fact]
    public void FromText_Sets_TextLength_From_Source()
    {
        var ctx = TranslationRoutingContext.FromText("12345", "zh", "en");
        Assert.Equal(5, ctx.TextLength);
    }

    [Fact]
    public void FromText_Propagates_IsHighQualityMode()
    {
        var ctx = TranslationRoutingContext.FromText("test", "zh", "en", isHighQualityMode: true);
        Assert.True(ctx.IsHighQualityMode);
    }
}

public sealed class ModelSelectionPolicyRoutingContextTests
{
    private static readonly StaticModelCatalog Catalog = new();

    [Fact]
    public void SelectTranslationProfile_LocalText_StaysLocal_WithPreferLocal()
    {
        var context = new TranslationRoutingContext(TextLength: 50);
        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "en",
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: false,
            cloud: null,
            context: context);

        Assert.Equal(ModelExecutionKind.ChatCompletions, profile.ExecutionKind);
        Assert.Equal(ModelRuntimeKind.LlamaServer, profile.RuntimeKind);
    }

    [Theory]
    [InlineData(601)]
    [InlineData(800)]
    [InlineData(1500)]
    public void SelectTranslationProfile_LongText_EscalatesToCloud_WhenCloudConfigured(int textLength)
    {
        var cloud = new CloudModelPreferences(
            Enabled: true,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            TranslationModelId: "gpt-4o-mini",
            PostProcessingModelId: null);

        var context = new TranslationRoutingContext(TextLength: textLength);
        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "en",
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: false,
            cloud: cloud,
            context: context);

        Assert.Equal(ModelRuntimeKind.RemoteHttp, profile.RuntimeKind);
        Assert.Equal("gpt-4o-mini", profile.Id);
    }

    [Fact]
    public void SelectTranslationProfile_LongText_StaysLocal_WhenLocalOnly()
    {
        var cloud = new CloudModelPreferences(
            Enabled: true,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            TranslationModelId: "gpt-4o-mini",
            PostProcessingModelId: null);

        var context = new TranslationRoutingContext(TextLength: 700);
        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "en",
            routingMode: TranslationRoutingMode.LocalOnly,
            routeUnsupportedPairsToCloud: false,
            cloud: cloud,
            context: context);

        // LocalOnly is never overridden by context escalation
        Assert.Equal(ModelRuntimeKind.LlamaServer, profile.RuntimeKind);
    }

    [Fact]
    public void SelectTranslationProfile_RareLanguagePair_EscalatesToCloud()
    {
        var cloud = new CloudModelPreferences(
            Enabled: true,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            TranslationModelId: "gpt-4o",
            PostProcessingModelId: null);

        var context = new TranslationRoutingContext(
            TextLength: 50, IsRareLanguagePair: true);

        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "fr",
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: false,
            cloud: cloud,
            context: context);

        Assert.Equal(ModelRuntimeKind.RemoteHttp, profile.RuntimeKind);
    }

    [Fact]
    public void SelectTranslationProfile_HighQualityMode_EscalatesToCloud()
    {
        var cloud = new CloudModelPreferences(
            Enabled: true,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            TranslationModelId: "gpt-4o",
            PostProcessingModelId: null);

        var context = new TranslationRoutingContext(
            TextLength: 30, IsHighQualityMode: true);

        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "en",
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: false,
            cloud: cloud,
            context: context);

        Assert.Equal(ModelRuntimeKind.RemoteHttp, profile.RuntimeKind);
    }

    [Fact]
    public void SelectTranslationProfile_ShortText_StaysLocal_WhenPreferLocal()
    {
        var cloud = new CloudModelPreferences(
            Enabled: true,
            BaseUrl: "https://api.openai.com",
            ApiKey: "sk-test",
            TranslationModelId: "gpt-4o",
            PostProcessingModelId: null);

        var context = new TranslationRoutingContext(TextLength: 40); // ≤ 80

        var profile = ModelSelectionPolicy.SelectTranslationProfile(
            Catalog, null, "zh", "en",
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: false,
            cloud: cloud,
            context: context);

        Assert.Equal(ModelRuntimeKind.LlamaServer, profile.RuntimeKind);
    }
}
