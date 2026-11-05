using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.Cloud;

/// <summary>
/// Sends a role to the cloud only when the user switched it on in the settings, and only to the one enabled endpoint.
/// The settings and the API key (system secret store) are read on every request. An incomplete configuration is an
/// explicit error: the text is never quietly handled by another backend or another endpoint.
/// </summary>
public sealed class SettingsCloudRoutes(ISettingsService settings, ISecretStore secrets) : ICloudRoutes
{
    public async Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct)
    {
        var cloud = settings.Current.Cloud;
        var enabled = role switch
        {
            InferenceRole.Translator => cloud.Translate,
            InferenceRole.Cleaner => cloud.Clean,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        if (!enabled)
            return null;

        if (cloud.Active is not { } endpoint)
            throw new CloudNotConfiguredException("The cloud model is not configured: no cloud endpoint is enabled.");

        var key = await secrets.GetSecretAsync(endpoint.SecretId, ct).ConfigureAwait(false);
        try
        {
            return CloudRoute.Create(endpoint.BaseUrl, endpoint.Model, key ?? "", endpoint.Name);
        }
        catch (ArgumentException ex)
        {
            throw new CloudNotConfiguredException($"The cloud model is not configured: {ex.Message}", ex);
        }
    }
}
