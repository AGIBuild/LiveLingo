using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Services.Cloud;

public static class CloudProviderRuntimePresentation
{
    public static string? BuildSettingsStatusMessage(
        ILocalizationService? loc,
        SettingsModel settings,
        CloudProviderRuntimeSnapshot snapshot)
    {
        if (snapshot.Status == CloudProviderRuntimeStatus.Unknown)
            return null;

        var preferences = CoreOptionsSync.CreateCloudModelPreferences(settings);
        return BuildStatusMessage(loc, preferences, snapshot);
    }

    public static string? BuildStartupIssueMessage(
        ILocalizationService? loc,
        SettingsModel settings,
        CloudProviderRuntimeSnapshot snapshot)
    {
        if (!settings.Translation.CloudProvider.Enabled)
            return null;

        if (snapshot.Status == CloudProviderRuntimeStatus.Unknown)
            return null;

        var routingMode = Enum.TryParse<TranslationRoutingMode>(
            settings.Translation.ModelPolicy.RoutingMode,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : TranslationRoutingMode.PreferLocal;
        var cloudMatters = routingMode != TranslationRoutingMode.LocalOnly
            || settings.Translation.ModelPolicy.RouteUnsupportedPairsToCloud
            || settings.Translation.ModelPolicy.RoutePostProcessingToCloud;
        if (!cloudMatters)
            return null;

        var preferences = CoreOptionsSync.CreateCloudModelPreferences(settings);
        if (snapshot.Status != CloudProviderRuntimeStatus.Healthy)
            return BuildStatusMessage(loc, preferences, snapshot);

        if (settings.Translation.ModelPolicy.RoutePostProcessingToCloud &&
            snapshot.ValidationMode == CloudProviderValidationMode.DirectModelProbe &&
            HasDistinctPostProcessingModel(preferences))
        {
            return BuildStatusMessage(loc, preferences, snapshot);
        }

        return null;
    }

    private static string? BuildStatusMessage(
        ILocalizationService? loc,
        CloudModelPreferences preferences,
        CloudProviderRuntimeSnapshot snapshot)
    {
        var translationModelId = preferences.TranslationModelId?.Trim();
        var postProcessingModelId = preferences.ResolvePostProcessingModelId()?.Trim();

        return snapshot.Status switch
        {
            CloudProviderRuntimeStatus.Healthy => BuildHealthyMessage(
                loc,
                snapshot,
                translationModelId,
                postProcessingModelId),
            CloudProviderRuntimeStatus.InvalidConfiguration => T(
                loc,
                "cloud.status.invalidConfiguration",
                "Cloud provider configuration is incomplete. Set base URL, API key, and a translation model in Settings."),
            CloudProviderRuntimeStatus.Unavailable when
                snapshot.ValidationMode == CloudProviderValidationMode.DirectModelProbe &&
                !string.IsNullOrWhiteSpace(translationModelId) => T(
                    loc,
                    "cloud.status.directUnavailable",
                    "Connection failed. This provider does not expose a model list, and direct validation of translation model '{0}' failed: {1}",
                    translationModelId,
                    snapshot.Message ?? T(loc, "cloud.status.unavailable", "Cloud provider validation failed.")),
            CloudProviderRuntimeStatus.Unavailable => snapshot.Message ??
                T(loc, "cloud.status.unavailable", "Cloud provider validation failed."),
            _ => snapshot.Message
        };
    }

    private static string? BuildHealthyMessage(
        ILocalizationService? loc,
        CloudProviderRuntimeSnapshot snapshot,
        string? translationModelId,
        string? postProcessingModelId)
    {
        if (snapshot.ValidationMode == CloudProviderValidationMode.DirectModelProbe &&
            !string.IsNullOrWhiteSpace(translationModelId))
        {
            if (!string.IsNullOrWhiteSpace(postProcessingModelId) &&
                !string.Equals(postProcessingModelId, translationModelId, StringComparison.OrdinalIgnoreCase))
            {
                return T(
                    loc,
                    "cloud.status.directHealthyWithPost",
                    "Connected. This provider does not expose a model list, so LiveLingo validated translation model '{0}' directly. Cloud post-processing model '{1}' could not be prevalidated.",
                    translationModelId,
                    postProcessingModelId);
            }

            return T(
                loc,
                "cloud.status.directHealthy",
                "Connected. This provider does not expose a model list, so LiveLingo validated translation model '{0}' directly.",
                translationModelId);
        }

        if (snapshot.ValidationMode == CloudProviderValidationMode.ModelCatalog)
        {
            return snapshot.Models.Count > 0
                ? T(loc, "cloud.status.catalogHealthy", "Connection succeeded. {0} models available.", snapshot.Models.Count)
                : T(loc, "cloud.status.catalogEmpty", "Connected, but the provider returned no models.");
        }

        return snapshot.Message;
    }

    private static bool HasDistinctPostProcessingModel(CloudModelPreferences preferences)
    {
        var resolvedPostModelId = preferences.ResolvePostProcessingModelId()?.Trim();
        return !string.IsNullOrWhiteSpace(resolvedPostModelId) &&
               !string.Equals(resolvedPostModelId, preferences.TranslationModelId?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string T(ILocalizationService? loc, string key, string fallback, params object[] args) =>
        loc?.T(key, args) ?? string.Format(fallback, args);
}
