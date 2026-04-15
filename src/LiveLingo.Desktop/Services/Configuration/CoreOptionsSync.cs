using LiveLingo.Core;
using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.Services.Configuration;

/// <summary>
/// Pushes user settings into the live <see cref="CoreOptions"/> instance (same reference as <see cref="IOptions{CoreOptions}"/>).
/// </summary>
public static class CoreOptionsSync
{
    public static void ApplyFromSettings(SettingsModel settings, CoreOptions target, IModelManager? modelManager = null)
    {
        if (!string.IsNullOrWhiteSpace(settings.Advanced.ModelStoragePath))
        {
            try
            {
                target.ModelStoragePath = Path.GetFullPath(settings.Advanced.ModelStoragePath.Trim());
            }
            catch
            {
                target.ModelStoragePath = settings.Advanced.ModelStoragePath.Trim();
            }
        }
        else
        {
            target.ModelStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveLingo",
                "models");
        }

        target.DefaultTargetLanguage = settings.Translation.DefaultTargetLanguage;
        target.ActiveTranslationModelId =
            string.IsNullOrWhiteSpace(settings.Translation.ModelPolicy.PreferredLocalTranslationModelId)
                ? settings.Translation.ActiveTranslationModelId
                : settings.Translation.ModelPolicy.PreferredLocalTranslationModelId?.Trim();
        target.TranslationRoutingMode = ParseRoutingMode(settings.Translation.ModelPolicy.RoutingMode);
        target.RouteUnsupportedLanguagePairsToCloud = settings.Translation.ModelPolicy.RouteUnsupportedPairsToCloud;
        target.RoutePostProcessingToCloud = settings.Translation.ModelPolicy.RoutePostProcessingToCloud;
        target.CloudProviderEnabled = settings.Translation.CloudProvider.Enabled;
        target.CloudProviderBaseUrl = string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.BaseUrl)
            ? null
            : settings.Translation.CloudProvider.BaseUrl.Trim();
        target.CloudProviderApiKey = string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.ApiKey)
            ? null
            : settings.Translation.CloudProvider.ApiKey.Trim();
        target.CloudTranslationModelId = string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.TranslationModelId)
            ? null
            : settings.Translation.CloudProvider.TranslationModelId.Trim();
        target.CloudPostProcessingModelId = string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.PostProcessingModelId)
            ? null
            : settings.Translation.CloudProvider.PostProcessingModelId.Trim();
        target.InferenceThreads = settings.Advanced.InferenceThreads;
        target.HuggingFaceMirror = string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceMirror)
            ? null
            : settings.Advanced.HuggingFaceMirror.Trim();
        target.HuggingFaceToken = string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceToken)
            ? null
            : settings.Advanced.HuggingFaceToken.Trim();

        modelManager?.ResetHuggingfaceTransportFallback();
    }

    /// <summary>
    /// Whether persisted advanced fields that affect translation LLM load / download have changed.
    /// </summary>
    public static bool AdvancedSettingsAffectLlmLoad(AdvancedSettings before, AdvancedSettings after) =>
        before.InferenceThreads != after.InferenceThreads
        || !string.Equals(
            NormalizePathForCompare(before.ModelStoragePath),
            NormalizePathForCompare(after.ModelStoragePath),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.HuggingFaceMirror ?? "", after.HuggingFaceMirror ?? "", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(before.HuggingFaceToken ?? "", after.HuggingFaceToken ?? "", StringComparison.Ordinal);

    public static CloudModelPreferences CreateCloudModelPreferences(SettingsModel settings) =>
        new(
            settings.Translation.CloudProvider.Enabled,
            string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.BaseUrl)
                ? null
                : settings.Translation.CloudProvider.BaseUrl.Trim(),
            string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.ApiKey)
                ? null
                : settings.Translation.CloudProvider.ApiKey.Trim(),
            string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.TranslationModelId)
                ? null
                : settings.Translation.CloudProvider.TranslationModelId.Trim(),
            string.IsNullOrWhiteSpace(settings.Translation.CloudProvider.PostProcessingModelId)
                ? null
                : settings.Translation.CloudProvider.PostProcessingModelId.Trim());

    public static string NormalizePathForCompare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static TranslationRoutingMode ParseRoutingMode(string? routingMode) =>
        Enum.TryParse<TranslationRoutingMode>(routingMode, ignoreCase: true, out var parsed)
            ? parsed
            : TranslationRoutingMode.PreferLocal;
}
