using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Services.Configuration;

public static class SettingsSecretCoordinator
{
    public const string CloudProviderApiKeySlot = "cloud-provider-api-key";
    public const string HuggingFaceTokenSlot = "huggingface-access-token";

    public static SettingsModel CreatePersistableSnapshot(SettingsModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = settings.DeepClone();
        snapshot.Translation.CloudProvider.ApiKey = null;
        snapshot.Advanced.HuggingFaceToken = null;
        return snapshot;
    }

    public static async Task PersistSecretsAsync(
        SettingsModel settings,
        ISecretStore secretStore,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretStore);

        await PersistCloudApiKeyAsync(settings.Translation.CloudProvider, secretStore, ct).ConfigureAwait(false);
        await PersistHuggingFaceTokenAsync(settings.Advanced, secretStore, ct).ConfigureAwait(false);
    }

    public static async Task<bool> MigrateAndHydrateAsync(
        SettingsModel settings,
        ISecretStore secretStore,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretStore);

        var changed = false;
        changed |= await MigrateAndHydrateCloudApiKeyAsync(settings.Translation.CloudProvider, secretStore, ct).ConfigureAwait(false);
        changed |= await MigrateAndHydrateHuggingFaceTokenAsync(settings.Advanced, secretStore, ct).ConfigureAwait(false);
        return changed;
    }

    private static async Task PersistCloudApiKeyAsync(
        CloudProviderSettings cloudProvider,
        ISecretStore secretStore,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cloudProvider.ApiKey))
        {
            if (!string.IsNullOrWhiteSpace(cloudProvider.ApiKeySecretId))
                await secretStore.DeleteSecretAsync(cloudProvider.ApiKeySecretId, ct).ConfigureAwait(false);

            cloudProvider.ApiKeySecretId = null;
            return;
        }

        cloudProvider.ApiKeySecretId ??= CloudProviderApiKeySlot;
        await secretStore.SetSecretAsync(cloudProvider.ApiKeySecretId, cloudProvider.ApiKey.Trim(), ct).ConfigureAwait(false);
    }

    private static async Task PersistHuggingFaceTokenAsync(
        AdvancedSettings advanced,
        ISecretStore secretStore,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(advanced.HuggingFaceToken))
        {
            if (!string.IsNullOrWhiteSpace(advanced.HuggingFaceTokenSecretId))
                await secretStore.DeleteSecretAsync(advanced.HuggingFaceTokenSecretId, ct).ConfigureAwait(false);

            advanced.HuggingFaceTokenSecretId = null;
            return;
        }

        advanced.HuggingFaceTokenSecretId ??= HuggingFaceTokenSlot;
        await secretStore.SetSecretAsync(advanced.HuggingFaceTokenSecretId, advanced.HuggingFaceToken.Trim(), ct).ConfigureAwait(false);
    }

    private static async Task<bool> MigrateAndHydrateCloudApiKeyAsync(
        CloudProviderSettings cloudProvider,
        ISecretStore secretStore,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cloudProvider.ApiKey))
        {
            cloudProvider.ApiKeySecretId ??= CloudProviderApiKeySlot;
            await secretStore.SetSecretAsync(cloudProvider.ApiKeySecretId, cloudProvider.ApiKey.Trim(), ct).ConfigureAwait(false);
            return true;
        }

        if (string.IsNullOrWhiteSpace(cloudProvider.ApiKeySecretId))
            return false;

        var apiKey = await secretStore.GetSecretAsync(cloudProvider.ApiKeySecretId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            cloudProvider.ApiKeySecretId = null;
            return true;
        }

        cloudProvider.ApiKey = apiKey;
        return false;
    }

    private static async Task<bool> MigrateAndHydrateHuggingFaceTokenAsync(
        AdvancedSettings advanced,
        ISecretStore secretStore,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(advanced.HuggingFaceToken))
        {
            advanced.HuggingFaceTokenSecretId ??= HuggingFaceTokenSlot;
            await secretStore.SetSecretAsync(advanced.HuggingFaceTokenSecretId, advanced.HuggingFaceToken.Trim(), ct).ConfigureAwait(false);
            return true;
        }

        if (string.IsNullOrWhiteSpace(advanced.HuggingFaceTokenSecretId))
            return false;

        var token = await secretStore.GetSecretAsync(advanced.HuggingFaceTokenSecretId, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            advanced.HuggingFaceTokenSecretId = null;
            return true;
        }

        advanced.HuggingFaceToken = token;
        return false;
    }
}
