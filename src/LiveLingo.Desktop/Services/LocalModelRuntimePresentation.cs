using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Services;

public static class LocalModelRuntimePresentation
{
    public static string? BuildSettingsStatusMessage(
        ILocalizationService? loc,
        ModelLoadState state,
        ModelDescriptor? descriptor) => state switch
        {
            ModelLoadState.Loading => T(
                loc,
                "localModel.status.loading",
                "Loading {0}…",
                descriptor?.DisplayName ?? T(loc, "localModel.status.model", "model")),
            ModelLoadState.Loaded => T(
                loc,
                "localModel.status.ready",
                "{0} is loaded and ready.",
                descriptor?.DisplayName ?? T(loc, "localModel.status.model", "model")),
            _ => null
        };

    private static string T(ILocalizationService? loc, string key, string fallback, params object[] args) =>
        loc?.T(key, args) ?? string.Format(fallback, args);
}
