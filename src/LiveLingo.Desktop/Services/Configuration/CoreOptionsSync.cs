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
        target.ActiveTranslationModelId = settings.Translation.ActiveTranslationModelId;
        target.InferenceThreads = settings.Advanced.InferenceThreads;
        target.HuggingFaceMirror = string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceMirror)
            ? null
            : settings.Advanced.HuggingFaceMirror.Trim();
        target.HuggingFaceToken = string.IsNullOrWhiteSpace(settings.Advanced.HuggingFaceToken)
            ? null
            : settings.Advanced.HuggingFaceToken.Trim();

        if (!string.IsNullOrWhiteSpace(settings.Advanced.LlamaNativeSearchPath))
        {
            try
            {
                target.LlamaNativeSearchPath = Path.GetFullPath(settings.Advanced.LlamaNativeSearchPath.Trim());
            }
            catch
            {
                target.LlamaNativeSearchPath = settings.Advanced.LlamaNativeSearchPath.Trim();
            }
        }
        else
        {
            target.LlamaNativeSearchPath = null;
        }

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

    /// <summary>
    /// LLama native search path is read only at process start; changing it requires a full app restart.
    /// </summary>
    public static bool AdvancedLlamaNativePathChanged(AdvancedSettings before, AdvancedSettings after) =>
        !string.Equals(
            NormalizePathForCompare(before.LlamaNativeSearchPath),
            NormalizePathForCompare(after.LlamaNativeSearchPath),
            StringComparison.OrdinalIgnoreCase);

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
}
