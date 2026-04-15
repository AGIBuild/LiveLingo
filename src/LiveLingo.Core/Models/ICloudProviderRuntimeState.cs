using System.Security.Cryptography;
using System.Text;

namespace LiveLingo.Core.Models;

public enum CloudProviderRuntimeStatus
{
    Unknown,
    Healthy,
    InvalidConfiguration,
    InvalidModelSelection,
    Unavailable
}

public enum CloudProviderValidationMode
{
    Unknown,
    ModelCatalog,
    DirectModelProbe
}

public sealed record CloudProviderRuntimeSnapshot(
    string? ConfigurationFingerprint,
    CloudProviderRuntimeStatus Status,
    CloudProviderValidationMode ValidationMode,
    string? Message,
    DateTimeOffset? LastValidatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    IReadOnlyList<CloudProviderModelInfo> Models)
{
    public static CloudProviderRuntimeSnapshot Unknown { get; } = new(
        null,
        CloudProviderRuntimeStatus.Unknown,
        CloudProviderValidationMode.Unknown,
        null,
        null,
        null,
        []);

    public bool Matches(CloudModelPreferences? preferences) =>
        string.Equals(
            ConfigurationFingerprint,
            CloudProviderConfigurationFingerprint.Create(preferences),
            StringComparison.Ordinal);

    public bool IsExpired(DateTimeOffset now) =>
        ExpiresAtUtc is { } expiresAt && expiresAt <= now;
}

public sealed record CloudProviderRoutingState(
    bool HasValidation,
    bool IsHealthy,
    string? Message,
    IReadOnlySet<string> AvailableModelIds)
{
    public static CloudProviderRoutingState Unknown { get; } = new(false, false, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool HasValidatedModels => HasValidation && AvailableModelIds.Count > 0;

    public bool IsModelAvailable(string modelId) =>
        AvailableModelIds.Contains(modelId);
}

public interface ICloudProviderRuntimeState
{
    CloudProviderRuntimeSnapshot Current { get; }
    event Action<CloudProviderRuntimeSnapshot>? Changed;
    CloudProviderRoutingState GetRoutingState(CloudModelPreferences? preferences);
    Task<CloudProviderRuntimeSnapshot> RefreshAsync(CloudModelPreferences? preferences, CancellationToken ct = default);
}

public sealed class NullCloudProviderRuntimeState : ICloudProviderRuntimeState
{
    public CloudProviderRuntimeSnapshot Current => CloudProviderRuntimeSnapshot.Unknown;
    public event Action<CloudProviderRuntimeSnapshot>? Changed
    {
        add { }
        remove { }
    }
    public CloudProviderRoutingState GetRoutingState(CloudModelPreferences? preferences) => CloudProviderRoutingState.Unknown;
    public Task<CloudProviderRuntimeSnapshot> RefreshAsync(CloudModelPreferences? preferences, CancellationToken ct = default) =>
        Task.FromResult(Current);
}

public static class CloudProviderConfigurationFingerprint
{
    public static string Create(CloudModelPreferences? preferences)
    {
        if (preferences is not { Enabled: true })
            return string.Empty;

        var payload = string.Join("|",
            preferences.Enabled ? "1" : "0",
            preferences.BaseUrl?.Trim() ?? string.Empty,
            preferences.TranslationModelId?.Trim() ?? string.Empty,
            preferences.ResolvePostProcessingModelId()?.Trim() ?? string.Empty,
            HashSecret(preferences.ApiKey));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string HashSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return string.Empty;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim())));
    }
}
