using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.Cloud;

/// <summary>
/// Sends a role to the cloud only when the user explicitly assigned a cloud endpoint to that role.
/// The settings and the API key (system secret store) are read on every request. An incomplete or dangling configuration
/// is an explicit error: text is never quietly handled by the local model or by another endpoint.
/// </summary>
public sealed class SettingsCloudRoutes(ISettingsService settings, ISecretStore secrets) : ICloudRoutes
{
    public async Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct)
    {
        var cloud = settings.Current.Cloud;
        var endpointId = role switch
        {
            InferenceRole.Translator => cloud.TranslatorEndpointId,
            InferenceRole.Cleaner => cloud.CleanerEndpointId,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        if (string.IsNullOrEmpty(endpointId))
            return null;

        if (cloud.Endpoints.FirstOrDefault(item => item.Id == endpointId) is not { } endpoint)
            throw new CloudNotConfiguredException("The cloud model is not configured: the selected cloud endpoint does not exist.");

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

    public async Task<SpeechCloudRoute?> GetSpeechAsync(CancellationToken ct)
    {
        var cloud = settings.Current.Cloud;
        var endpointId = cloud.SpeechEndpointId;
        if (string.IsNullOrEmpty(endpointId))
            return null;

        if (cloud.Endpoints.FirstOrDefault(item => item.Id == endpointId) is not { } endpoint)
            throw new CloudNotConfiguredException("The cloud speech model is not configured: the selected cloud endpoint does not exist.");

        var key = await secrets.GetSecretAsync(endpoint.SecretId, ct).ConfigureAwait(false);
        try
        {
            return SpeechCloudRoute.Create(endpoint.BaseUrl, endpoint.Model, key ?? "", endpoint.Name);
        }
        catch (ArgumentException ex)
        {
            throw new CloudNotConfiguredException($"The cloud speech model is not configured: {ex.Message}", ex);
        }
    }
}
