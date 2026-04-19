using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Drives the "Test connection" + model-tag enumeration round-trip against
/// the user-managed Ollama daemon. Pure async over <see cref="IOllamaProbeService"/>;
/// has no observable state of its own, so the ViewModel keeps owning
/// <c>IsTestingOllamaProvider</c> / <c>OllamaProviderStatusMessage</c> /
/// <c>DiscoveredOllamaModels</c> for binding.
/// </summary>
internal interface IOllamaProviderProbeOrchestrator
{
    Task<OllamaProbeOutcome> ProbeAsync(string? baseUrl, CancellationToken ct);
}

/// <summary>
/// Result of an Ollama probe. <see cref="Models"/> is empty when the connection
/// failed or the probe service is unavailable.
/// </summary>
internal sealed record OllamaProbeOutcome(
    OllamaProbeOutcomeKind Kind,
    string Message,
    IReadOnlyList<OllamaModelInfo> Models);

internal enum OllamaProbeOutcomeKind
{
    /// <summary>The probe service was not registered in DI.</summary>
    ProbeUnavailable,

    /// <summary>The base URL was missing or whitespace.</summary>
    InvalidBaseUrl,

    /// <summary>Daemon was reached and tags were enumerated.</summary>
    Success,

    /// <summary>Daemon was unreachable or returned a non-success response.</summary>
    Failure,
}
