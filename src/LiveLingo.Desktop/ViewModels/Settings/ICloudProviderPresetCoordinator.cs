using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Mediates between the cloud-provider preset combobox and the underlying
/// <see cref="CloudProviderSettings"/>. Applying a preset rewrites the base URL,
/// translation/post-process model placeholders; conversely, editing the base URL
/// re-infers the preset id. The coordinator owns the two re-entrancy guards
/// (<c>_isApplyingCloudPreset</c> / <c>_isInferringCloudPreset</c>) so the
/// PropertyChanged echoes don't loop.
/// </summary>
internal interface ICloudProviderPresetCoordinator
{
    /// <summary>
    /// Raised whenever the placeholder texts that depend on the active preset change,
    /// so the ViewModel can re-broadcast PropertyChanged for the affected bindings.
    /// </summary>
    event Action? PresentationChanged;

    /// <summary>
    /// True when the coordinator is currently rewriting the cloud-provider settings
    /// because of a preset change or a base-URL inference. The ViewModel uses this to
    /// suppress its own dirty-tracking from the synthetic PropertyChanged events.
    /// </summary>
    bool IsRewritingPresetFields { get; }

    /// <summary>
    /// Applies the preset identified by <see cref="CloudProviderSettings.PresetId"/>.
    /// No-op when the preset is "Custom".
    /// </summary>
    void ApplyPreset(CloudProviderSettings cloudProvider);

    /// <summary>
    /// Re-syncs the preset id from the (possibly user-edited) base URL.
    /// </summary>
    void SyncPresetFromBaseUrl(CloudProviderSettings cloudProvider);

    /// <summary>
    /// Returns the preset currently selected by the working copy.
    /// </summary>
    CloudProviderPreset GetSelectedPreset(CloudProviderSettings cloudProvider);
}
