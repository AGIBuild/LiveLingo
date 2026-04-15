using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Tests.Services.Configuration;

public sealed class SettingsSecretCoordinatorTests
{
    [Fact]
    public async Task PersistSecretsAsync_AssignsReferencesAndStoresSecrets()
    {
        var settings = SettingsModel.CreateDefault();
        settings.Translation.CloudProvider.ApiKey = "sk-secret";
        settings.Advanced.HuggingFaceToken = "hf-secret";
        var store = new InMemorySecretStore();

        await SettingsSecretCoordinator.PersistSecretsAsync(settings, store, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.ApiKeySecretId));
        Assert.False(string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceTokenSecretId));
        Assert.Equal(
            "sk-secret",
            await store.GetSecretAsync(settings.Translation.CloudProvider.ApiKeySecretId!, TestContext.Current.CancellationToken));
        Assert.Equal(
            "hf-secret",
            await store.GetSecretAsync(settings.Advanced.HuggingFaceTokenSecretId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MigrateAndHydrateAsync_MigratesLegacyPlaintextSecrets()
    {
        var settings = SettingsModel.CreateDefault();
        settings.Translation.CloudProvider.ApiKey = "sk-legacy";
        settings.Advanced.HuggingFaceToken = "hf-legacy";
        var store = new InMemorySecretStore();

        var changed = await SettingsSecretCoordinator.MigrateAndHydrateAsync(
            settings,
            store,
            TestContext.Current.CancellationToken);

        Assert.True(changed);
        Assert.False(string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.ApiKeySecretId));
        Assert.False(string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceTokenSecretId));
        Assert.Equal(
            "sk-legacy",
            await store.GetSecretAsync(settings.Translation.CloudProvider.ApiKeySecretId!, TestContext.Current.CancellationToken));
        Assert.Equal(
            "hf-legacy",
            await store.GetSecretAsync(settings.Advanced.HuggingFaceTokenSecretId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MigrateAndHydrateAsync_LoadsSecretsFromExistingReferences()
    {
        var settings = SettingsModel.CreateDefault();
        settings.Translation.CloudProvider.ApiKeySecretId = "cloud-slot";
        settings.Advanced.HuggingFaceTokenSecretId = "hf-slot";
        var store = new InMemorySecretStore();
        await store.SetSecretAsync("cloud-slot", "sk-restored", TestContext.Current.CancellationToken);
        await store.SetSecretAsync("hf-slot", "hf-restored", TestContext.Current.CancellationToken);

        var changed = await SettingsSecretCoordinator.MigrateAndHydrateAsync(
            settings,
            store,
            TestContext.Current.CancellationToken);

        Assert.False(changed);
        Assert.Equal("sk-restored", settings.Translation.CloudProvider.ApiKey);
        Assert.Equal("hf-restored", settings.Advanced.HuggingFaceToken);
    }

    [Fact]
    public void CreatePersistableSnapshot_StripsSecretValuesButKeepsReferences()
    {
        var settings = SettingsModel.CreateDefault();
        settings.Translation.CloudProvider.ApiKey = "sk-secret";
        settings.Translation.CloudProvider.ApiKeySecretId = "cloud-slot";
        settings.Advanced.HuggingFaceToken = "hf-secret";
        settings.Advanced.HuggingFaceTokenSecretId = "hf-slot";

        var snapshot = SettingsSecretCoordinator.CreatePersistableSnapshot(settings);

        Assert.Null(snapshot.Translation.CloudProvider.ApiKey);
        Assert.Equal("cloud-slot", snapshot.Translation.CloudProvider.ApiKeySecretId);
        Assert.Null(snapshot.Advanced.HuggingFaceToken);
        Assert.Equal("hf-slot", snapshot.Advanced.HuggingFaceTokenSecretId);
    }
}
