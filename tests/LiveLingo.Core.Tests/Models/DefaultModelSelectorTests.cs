using LiveLingo.Core.Models;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Models;

public sealed class DefaultModelSelectorTests
{
    [Fact]
    public void SelectTranslationProfile_UsesConfiguredChatTranslationProfile()
    {
        var selector = CreateSelector(ModelRegistry.Qwen25_7B.Id);

        var profile = selector.SelectTranslationProfile("zh", "en");

        Assert.Equal(ModelRegistry.Qwen25_7B.Id, profile.Id);
    }

    [Fact]
    public void SelectTranslationProfile_FallsBackToDefault_WhenConfiguredProfileIsNotChatTranslation()
    {
        var selector = CreateSelector(ModelRegistry.MarianEnZh.Id);

        var profile = selector.SelectTranslationProfile("en", "zh");

        Assert.Equal(ModelRegistry.Qwen35_9B.Id, profile.Id);
    }

    [Fact]
    public void SelectPostProcessingProfile_UsesConfiguredChatTranslationProfile_WhenAvailable()
    {
        var selector = CreateSelector(ModelRegistry.Qwen25_7B.Id);

        var profile = selector.SelectPostProcessingProfile();

        Assert.Equal(ModelRegistry.Qwen25_7B.Id, profile.Id);
    }

    [Fact]
    public void SelectPostProcessingProfile_FallsBackToDedicatedPostProcessingModel_WhenConfiguredProfileIsNotChatTranslation()
    {
        var selector = CreateSelector(ModelRegistry.MarianZhEn.Id);

        var profile = selector.SelectPostProcessingProfile();

        Assert.Equal(ModelRegistry.Qwen25_15B.Id, profile.Id);
    }

    [Fact]
    public void SelectTranslationProfile_UsesCloudProfile_WhenRoutingModeIsCloudOnly()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.CloudOnly,
            cloudEnabled: true,
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudApiKey: "sk-test",
            cloudBaseUrl: "https://api.openai.com/v1");

        var profile = selector.SelectTranslationProfile("zh", "en");

        Assert.Equal("gpt-4.1-mini", profile.Id);
        Assert.Equal(ModelProviderKind.OpenAICompatible, profile.ProviderKind);
        Assert.Equal(ModelRuntimeKind.RemoteHttp, profile.RuntimeKind);
        Assert.True(profile.SupportsAllLanguages);
    }

    [Fact]
    public void SelectTranslationProfile_FallsBackToCloud_WhenLocalPairUnsupported()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferLocal,
            routeUnsupportedPairsToCloud: true,
            cloudEnabled: true,
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudApiKey: "sk-test",
            cloudBaseUrl: "https://api.openai.com/v1");

        var profile = selector.SelectTranslationProfile("zh", "it");

        Assert.Equal("gpt-4.1-mini", profile.Id);
        Assert.Equal(ModelProviderKind.OpenAICompatible, profile.ProviderKind);
    }

    [Fact]
    public void SelectTranslationProfile_Throws_WhenCloudOnlyButCloudNotConfigured()
    {
        var selector = CreateSelector(routingMode: TranslationRoutingMode.CloudOnly);

        var ex = Assert.Throws<InvalidOperationException>(() => selector.SelectTranslationProfile("zh", "en"));

        Assert.Contains("Cloud translation provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectPostProcessingProfile_UsesCloudPostProcessingProfile_WhenPreferCloud()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferCloud,
            cloudEnabled: true,
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudPostProcessingModelId: "gpt-4.1-nano",
            cloudApiKey: "sk-test",
            cloudBaseUrl: "https://api.openai.com/v1");

        var profile = selector.SelectPostProcessingProfile();

        Assert.Equal("gpt-4.1-nano", profile.Id);
        Assert.Equal(ModelTaskType.PostProcessing, profile.TaskType);
    }

    [Fact]
    public void SelectTranslationProfile_FallsBackToLocal_WhenPreferCloudButRuntimeValidationFailed()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferCloud,
            cloudEnabled: true,
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudApiKey: "sk-test",
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudRuntimeState: new TestCloudProviderRuntimeState(
                new CloudProviderRoutingState(
                    HasValidation: true,
                    IsHealthy: false,
                    Message: "Cloud provider is unreachable.",
                    AvailableModelIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase))));

        var profile = selector.SelectTranslationProfile("zh", "en");

        Assert.Equal(ModelRegistry.Qwen35_9B.Id, profile.Id);
    }

    [Fact]
    public void SelectTranslationProfile_Throws_WhenCloudOnlyAndValidatedModelMissing()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.CloudOnly,
            cloudEnabled: true,
            cloudTranslationModelId: "gpt-4.1-mini",
            cloudApiKey: "sk-test",
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudRuntimeState: new TestCloudProviderRuntimeState(
                new CloudProviderRoutingState(
                    HasValidation: true,
                    IsHealthy: true,
                    Message: null,
                    AvailableModelIds: new HashSet<string>(["gpt-4.1"], StringComparer.OrdinalIgnoreCase)))); 

        var ex = Assert.Throws<InvalidOperationException>(() => selector.SelectTranslationProfile("zh", "en"));

        Assert.Contains("gpt-4.1-mini", ex.Message, StringComparison.Ordinal);
    }

    private static DefaultModelSelector CreateSelector(
        string? activeTranslationModelId = null,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routeUnsupportedPairsToCloud = false,
        bool routePostProcessingToCloud = false,
        bool cloudEnabled = false,
        string? cloudBaseUrl = null,
        string? cloudApiKey = null,
        string? cloudTranslationModelId = null,
        string? cloudPostProcessingModelId = null,
        ICloudProviderRuntimeState? cloudRuntimeState = null)
    {
        var catalog = new StaticModelCatalog();
        var options = Options.Create(new CoreOptions
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
        });

        return new DefaultModelSelector(catalog, options, cloudRuntimeState ?? new NullCloudProviderRuntimeState());
    }

    private sealed class TestCloudProviderRuntimeState(CloudProviderRoutingState routingState) : ICloudProviderRuntimeState
    {
        public CloudProviderRuntimeSnapshot Current => CloudProviderRuntimeSnapshot.Unknown;
        public event Action<CloudProviderRuntimeSnapshot>? Changed
        {
            add { }
            remove { }
        }
        public CloudProviderRoutingState GetRoutingState(CloudModelPreferences? preferences) => routingState;
        public Task<CloudProviderRuntimeSnapshot> RefreshAsync(CloudModelPreferences? preferences, CancellationToken ct = default) =>
            Task.FromResult(Current);
    }
}
