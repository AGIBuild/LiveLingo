using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Tests.Settings;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services.Cloud;

public class SettingsCloudRoutesTests
{
    private static (SettingsCloudRoutes Routes, SettingsModel Model, ISecretStore Secrets) Create(string? key = "sk-test")
    {
        var model = new SettingsModel();
        model.Cloud.Endpoints =
        [
            CloudTestData.Endpoint("a", "Foundry", "https://example.openai.azure.com/openai/v1", "gpt-4.1-mini"),
            CloudTestData.Endpoint("b", "Other", "https://other.example.com/v1", "other-model")
        ];
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(_ => model);
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetSecretAsync(CloudTestData.SecretId("a"), Arg.Any<CancellationToken>()).Returns(key);
        secrets.GetSecretAsync(CloudTestData.SecretId("b"), Arg.Any<CancellationToken>()).Returns("sk-other");
        return (new SettingsCloudRoutes(settings, secrets), model, secrets);
    }

    [Fact]
    public async Task ByDefault_EveryRoleStaysLocal()
    {
        var (routes, _, secrets) = Create();

        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
        Assert.Null(await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None));
        await secrets.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OnlyTheAssignedRoles_GoToTheCloud()
    {
        var (routes, model, _) = Create();
        model.Cloud.TranslatorEndpointId = "a";

        var translator = await routes.GetAsync(InferenceRole.Translator, CancellationToken.None);

        Assert.Equal("https://example.openai.azure.com/openai/v1/chat/completions", translator!.Endpoint.ToString());
        Assert.Equal("gpt-4.1-mini", translator.Model);
        Assert.Equal("sk-test", translator.ApiKey);
        Assert.Null(await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None));
    }

    [Fact]
    public async Task CleanerAndTranslator_CanIndependentlyUseDifferentCloudEndpoints()
    {
        var (routes, model, _) = Create();
        model.Cloud.TranslatorEndpointId = "a";
        model.Cloud.CleanerEndpointId = "b";

        var translator = await routes.GetAsync(InferenceRole.Translator, CancellationToken.None);
        var cleaner = await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None);

        Assert.Equal(("gpt-4.1-mini", "sk-test"), (translator!.Model, translator.ApiKey));
        Assert.Equal("https://other.example.com/v1/chat/completions", cleaner!.Endpoint.ToString());
        Assert.Equal(("other-model", "sk-other"), (cleaner.Model, cleaner.ApiKey));
    }

    [Fact]
    public async Task AnEnabledRouteWithoutAKey_IsAnExplicitError_NotASilentLocalFallback()
    {
        var (routes, model, _) = Create(key: null);
        model.Cloud.TranslatorEndpointId = "a";

        var ex = await Assert.ThrowsAsync<CloudNotConfiguredException>(
            () => routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task AnEnabledRouteWithAnUnusableUrl_IsAnExplicitError()
    {
        var (routes, model, _) = Create();
        model.Cloud.TranslatorEndpointId = "a";
        model.Cloud.Endpoints[0].BaseUrl = "http://not-secure.example.com/v1";

        await Assert.ThrowsAsync<CloudNotConfiguredException>(
            () => routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
    }

    [Fact]
    public async Task ADanglingEndpointReference_IsAnExplicitError_NotAnotherEndpointOrLocal()
    {
        var (routes, model, secrets) = Create();
        model.Cloud.TranslatorEndpointId = "missing";

        var ex = await Assert.ThrowsAsync<CloudNotConfiguredException>(
            () => routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
        await secrets.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SettingsChanges_ApplyToTheNextRequest()
    {
        var (routes, model, _) = Create();
        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        model.Cloud.TranslatorEndpointId = "a";
        Assert.NotNull(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        model.Cloud.TranslatorEndpointId = "";
        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
    }
}
