using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels.Settings;

internal sealed class OllamaProviderProbeOrchestrator : IOllamaProviderProbeOrchestrator
{
    private readonly IOllamaProbeService? _probe;
    private readonly ISettingsLocalizationHelper _localization;

    public OllamaProviderProbeOrchestrator(IOllamaProbeService? probe, ISettingsLocalizationHelper localization)
    {
        _probe = probe;
        _localization = localization;
    }

    public async Task<OllamaProbeOutcome> ProbeAsync(string? baseUrl, CancellationToken ct)
    {
        if (_probe is null)
        {
            return new OllamaProbeOutcome(
                OllamaProbeOutcomeKind.ProbeUnavailable,
                _localization.Translate("settings.ai.ollamaProbeUnavailable",
                    "Ollama probe service is not available in this build."),
                []);
        }

        var trimmed = baseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new OllamaProbeOutcome(
                OllamaProbeOutcomeKind.InvalidBaseUrl,
                _localization.Translate("settings.ai.ollamaInvalidBaseUrl", "Ollama base URL is required."),
                []);
        }

        var request = new OllamaProbeRequest(trimmed);
        var connection = await _probe.TestConnectionAsync(request, ct).ConfigureAwait(false);
        if (!connection.IsSuccess)
            return new OllamaProbeOutcome(OllamaProbeOutcomeKind.Failure, connection.Message, []);

        var catalog = await _probe.GetModelCatalogAsync(request, ct).ConfigureAwait(false);
        return new OllamaProbeOutcome(OllamaProbeOutcomeKind.Success, connection.Message, catalog.Models);
    }
}
