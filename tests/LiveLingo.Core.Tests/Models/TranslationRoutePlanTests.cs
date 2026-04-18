using LiveLingo.Core.Models;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Models;

/// <summary>
/// Verifies <see cref="ModelSelectionPolicy.BuildTranslationRoutePlan"/> produces
/// the correct ordered candidates for each routing mode and preference combination.
/// These tests exercise the full <see cref="DefaultModelSelector"/> → policy path.
/// </summary>
public sealed class TranslationRoutePlanTests
{
    [Fact]
    public void PreferLocal_WithCloudAndOllamaEnabled_PrimaryIsOllama_FallbacksAreBuiltinLocalThenCloud()
    {
        // An explicitly configured Ollama translation tag is an opt-in: users
        // that enabled Ollama expect their pulled model to run first, with the
        // built-in llama.cpp catalog acting as the local fallback and cloud as
        // the last-resort network degradation path.
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferLocal,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini",
            ollamaEnabled: true,
            ollamaTranslationModelId: "gemma3:4b");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Equal(3, plan.Candidates.Count);
        Assert.Equal(TranslationRouteTier.Ollama, plan.Candidates[0].Tier);
        Assert.Equal(TranslationRouteTier.Local, plan.Candidates[1].Tier);
        Assert.Equal(TranslationRouteTier.Cloud, plan.Candidates[2].Tier);
    }

    [Fact]
    public void PreferLocal_WithOnlyCloudEnabled_PrimaryIsLocal_FallbackIsCloud()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferLocal,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Equal(2, plan.Candidates.Count);
        Assert.Equal(TranslationRouteTier.Local, plan.Candidates[0].Tier);
        Assert.Equal(TranslationRouteTier.Cloud, plan.Candidates[1].Tier);
    }

    [Fact]
    public void PreferLocal_WithNoCloud_PlanContainsOnlyLocal()
    {
        var selector = CreateSelector(routingMode: TranslationRoutingMode.PreferLocal);

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Single(plan.Candidates);
        Assert.Equal(TranslationRouteTier.Local, plan.Candidates[0].Tier);
    }

    [Fact]
    public void PreferCloud_WithCloudAndLocal_PrimaryIsCloud_FallbacksIncludeLocal()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferCloud,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Equal(TranslationRouteTier.Cloud, plan.Candidates[0].Tier);
        Assert.Contains(plan.Candidates, c => c.Tier == TranslationRouteTier.Local);
    }

    [Fact]
    public void CloudOnly_ContainsOnlyCloudCandidate_EvenIfLocalAvailable()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.CloudOnly,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini",
            ollamaEnabled: true,
            ollamaTranslationModelId: "gemma3:4b");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Single(plan.Candidates);
        Assert.Equal(TranslationRouteTier.Cloud, plan.Candidates[0].Tier);
    }

    [Fact]
    public void LocalOnly_ContainsNoCloudCandidate_EvenIfCloudConfigured()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.LocalOnly,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.DoesNotContain(plan.Candidates, c => c.Tier == TranslationRouteTier.Cloud);
    }

    [Fact]
    public void LocalOnly_WithOllamaEnabled_OllamaIsPrimaryAndNoCloudFallback()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.LocalOnly,
            ollamaEnabled: true,
            ollamaTranslationModelId: "gemma3:4b");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        Assert.Single(plan.Candidates);
        Assert.Equal(TranslationRouteTier.Ollama, plan.Candidates[0].Tier);
    }

    [Fact]
    public void Plan_FirstTokenBudgets_AreTierSpecific()
    {
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferLocal,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        // Local budget is longer (cold-load), cloud budget is tighter (network only).
        var local = plan.Candidates.First(c => c.Tier == TranslationRouteTier.Local);
        var cloud = plan.Candidates.First(c => c.Tier == TranslationRouteTier.Cloud);
        Assert.True(local.FirstTokenBudget > cloud.FirstTokenBudget,
            $"Expected local budget ({local.FirstTokenBudget}) > cloud budget ({cloud.FirstTokenBudget}).");
    }

    [Fact]
    public void Plan_DoesNotDuplicateTheSameProfileTwice()
    {
        // PreferCloud primary = cloud; fallback attempt would also add cloud → must be deduped.
        var selector = CreateSelector(
            routingMode: TranslationRoutingMode.PreferCloud,
            cloudEnabled: true,
            cloudBaseUrl: "https://api.openai.com/v1",
            cloudApiKey: "sk-test",
            cloudTranslationModelId: "gpt-4o-mini");

        var plan = selector.BuildTranslationRoutePlan("zh", "en");

        var cloudCount = plan.Candidates.Count(c => c.Tier == TranslationRouteTier.Cloud);
        Assert.Equal(1, cloudCount);
    }

    private static DefaultModelSelector CreateSelector(
        string? activeTranslationModelId = null,
        TranslationRoutingMode routingMode = TranslationRoutingMode.PreferLocal,
        bool routeUnsupportedPairsToCloud = false,
        bool cloudEnabled = false,
        string? cloudBaseUrl = null,
        string? cloudApiKey = null,
        string? cloudTranslationModelId = null,
        bool ollamaEnabled = false,
        string ollamaBaseUrl = "http://localhost:11434",
        string? ollamaTranslationModelId = null)
    {
        var catalog = new StaticModelCatalog();
        var options = Options.Create(new CoreOptions
        {
            ActiveTranslationModelId = activeTranslationModelId,
            TranslationRoutingMode = routingMode,
            RouteUnsupportedLanguagePairsToCloud = routeUnsupportedPairsToCloud,
            CloudProviderEnabled = cloudEnabled,
            CloudProviderBaseUrl = cloudBaseUrl,
            CloudProviderApiKey = cloudApiKey,
            CloudTranslationModelId = cloudTranslationModelId,
            OllamaEnabled = ollamaEnabled,
            OllamaBaseUrl = ollamaBaseUrl,
            OllamaTranslationModelId = ollamaTranslationModelId
        });

        return new DefaultModelSelector(catalog, options, new NullCloudProviderRuntimeState());
    }
}
