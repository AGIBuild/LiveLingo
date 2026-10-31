using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services.Cloud;

public class SettingsCloudRoutesTests
{
    private static (SettingsCloudRoutes Routes, SettingsModel Model, ISecretStore Secrets) Create(string? key = "sk-test")
    {
        var model = new SettingsModel();
        model.Cloud.BaseUrl = "https://example.openai.azure.com/openai/v1";
        model.Cloud.Model = "gpt-4.1-mini";
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(_ => model);
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetSecretAsync(CloudSettings.ApiKeySecretId, Arg.Any<CancellationToken>()).Returns(key);
        return (new SettingsCloudRoutes(settings, secrets), model, secrets);
    }

    [Fact]
    public async Task ByDefault_EveryRoleStaysLocal()
    {
        var (routes, _, secrets) = Create();

        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
        Assert.Null(await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None));
        await secrets.DidNotReceiveWithAnyArgs().GetSecretAsync(default!, default);
    }

    [Fact]
    public async Task OnlyTheEnabledRoles_GoToTheCloud()
    {
        var (routes, model, _) = Create();
        model.Cloud.Translate = true;

        var translator = await routes.GetAsync(InferenceRole.Translator, CancellationToken.None);

        Assert.Equal("https://example.openai.azure.com/openai/v1/chat/completions", translator!.Endpoint.ToString());
        Assert.Equal("gpt-4.1-mini", translator.Model);
        Assert.Equal("sk-test", translator.ApiKey);
        Assert.Null(await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None));
    }

    [Fact]
    public async Task TheCleanerFollowsItsOwnSwitch()
    {
        var (routes, model, _) = Create();
        model.Cloud.Clean = true;

        Assert.NotNull(await routes.GetAsync(InferenceRole.Cleaner, CancellationToken.None));
        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
    }

    [Fact]
    public async Task AnEnabledRouteWithoutAKey_IsAnExplicitError_NotASilentLocalFallback()
    {
        var (routes, model, _) = Create(key: null);
        model.Cloud.Translate = true;

        var ex = await Assert.ThrowsAsync<CloudNotConfiguredException>(
            () => routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task AnEnabledRouteWithAnUnusableUrl_IsAnExplicitError()
    {
        var (routes, model, _) = Create();
        model.Cloud.Translate = true;
        model.Cloud.BaseUrl = "http://not-secure.example.com/v1";

        await Assert.ThrowsAsync<CloudNotConfiguredException>(
            () => routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
    }

    [Fact]
    public async Task SettingsChanges_ApplyToTheNextRequest()
    {
        var (routes, model, _) = Create();
        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        model.Cloud.Translate = true;
        Assert.NotNull(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));

        model.Cloud.Translate = false;
        Assert.Null(await routes.GetAsync(InferenceRole.Translator, CancellationToken.None));
    }
}
