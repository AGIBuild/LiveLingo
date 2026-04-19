using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class CloudProviderProbeOrchestrator : ICloudProviderProbeOrchestrator
{
    private readonly ICloudProviderRuntimeState _runtimeState;
    private readonly ILocalizationService? _localization;

    public CloudProviderProbeOrchestrator(
        ICloudProviderRuntimeState runtimeState,
        ILocalizationService? localization)
    {
        _runtimeState = runtimeState;
        _localization = localization;
    }

    public CloudProviderProbeOutcome BuildOutcomeFromCachedSnapshot(SettingsModel settings) =>
        BuildOutcome(_runtimeState.Current, settings);

    public async Task<CloudProviderProbeOutcome> RefreshAsync(SettingsModel settings, CancellationToken ct)
    {
        var preferences = CoreOptionsSync.CreateCloudModelPreferences(settings);
        var snapshot = await _runtimeState.RefreshAsync(preferences, ct).ConfigureAwait(false);
        return BuildOutcome(snapshot, settings);
    }

    private CloudProviderProbeOutcome BuildOutcome(CloudProviderRuntimeSnapshot? snapshot, SettingsModel settings)
    {
        if (snapshot is null)
            return new CloudProviderProbeOutcome(StatusMessage: null, Models: []);

        var preferences = CoreOptionsSync.CreateCloudModelPreferences(settings);
        if (!snapshot.Matches(preferences))
            return new CloudProviderProbeOutcome(StatusMessage: null, Models: []);

        var models = snapshot.Models
            .Select(model => new CloudProviderModelOption(model.Id, model.OwnedBy))
            .ToArray();
        var statusMessage = CloudProviderRuntimePresentation.BuildSettingsStatusMessage(_localization, settings, snapshot);
        return new CloudProviderProbeOutcome(statusMessage, models);
    }
}
