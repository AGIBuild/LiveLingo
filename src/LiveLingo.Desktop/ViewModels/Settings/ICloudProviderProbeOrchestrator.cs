using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Issues a cloud provider probe round-trip and folds the result back into a
/// <see cref="CloudProviderProbeOutcome"/> the ViewModel can render.
///
/// Wraps <see cref="LiveLingo.Desktop.Services.Cloud.ICloudProviderRuntimeState.RefreshAsync"/>
/// plus the snapshot→ViewModel translation (status text + discovered model list)
/// so the ViewModel does not need to know about <c>CloudProviderRuntimeSnapshot</c>
/// or <c>CloudProviderRuntimePresentation</c> directly.
/// </summary>
internal interface ICloudProviderProbeOrchestrator
{
    /// <summary>
    /// Reads the cached snapshot for the supplied settings (no network call) and
    /// turns it into a presentable outcome. Used right after LoadFromSettings.
    /// </summary>
    CloudProviderProbeOutcome BuildOutcomeFromCachedSnapshot(SettingsModel settings);

    /// <summary>
    /// Refreshes the snapshot via the runtime state service (typically a network
    /// call), then returns the resulting outcome.
    /// </summary>
    Task<CloudProviderProbeOutcome> RefreshAsync(SettingsModel settings, CancellationToken ct);
}

/// <summary>
/// Snapshot of "what the user sees in the cloud provider section of the Settings".
/// <see cref="Models"/> is empty when the snapshot doesn't match the current preferences
/// (i.e. cached for a different config) or when the provider is disabled.
/// </summary>
internal sealed record CloudProviderProbeOutcome(
    string? StatusMessage,
    IReadOnlyList<CloudProviderModelOption> Models);
