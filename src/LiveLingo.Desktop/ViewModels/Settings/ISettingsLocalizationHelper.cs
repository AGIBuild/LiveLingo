using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Translation lookup + canned localized option lists for the Settings window.
/// Centralises every <c>ILocalizationService</c> call so the ViewModel itself
/// does not have to deal with a possibly-null localizer or with repeating the
/// fallback-on-miss boilerplate.
/// </summary>
internal interface ISettingsLocalizationHelper
{
    string Translate(string key, string fallback);

    string Translate(string key, string fallback, params object[] args);

    /// <summary>
    /// Builds the localized SelectableOption lists used by combo-boxes in the Settings tabs.
    /// Recomputed on demand so changes to the active locale show up after a refresh.
    /// </summary>
    LocalizedSettingsOptions BuildSelectableOptions();

    string ResolveCloudPresetDisplayName(CloudProviderPreset preset);
}

internal sealed record LocalizedSettingsOptions(
    IReadOnlyList<SelectableOption> InjectionModes,
    IReadOnlyList<SelectableOption> PostProcessModes,
    IReadOnlyList<SelectableOption> RoutingModes,
    IReadOnlyList<SelectableOption> SttRoutingModes,
    IReadOnlyList<SelectableOption> CloudProviderPresets,
    IReadOnlyList<SelectableOption> LogLevels);
