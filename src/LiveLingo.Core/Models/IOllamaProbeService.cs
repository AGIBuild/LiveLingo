namespace LiveLingo.Core.Models;

/// <summary>
/// Probes a user-managed Ollama daemon for reachability and enumerates the
/// model tags that the user has already pulled locally. The daemon itself
/// is always user-managed; we only test the connection, never start/install it.
/// </summary>
public interface IOllamaProbeService
{
    Task<OllamaConnectionResult> TestConnectionAsync(
        OllamaProbeRequest request,
        CancellationToken ct = default);

    Task<OllamaModelCatalogResult> GetModelCatalogAsync(
        OllamaProbeRequest request,
        CancellationToken ct = default);
}

public sealed record OllamaProbeRequest(string BaseUrl);

public sealed record OllamaConnectionResult(bool IsSuccess, string Message, int ModelCount = 0);

public sealed record OllamaModelCatalogResult(IReadOnlyList<OllamaModelInfo> Models);

/// <summary>
/// Metadata for a single Ollama model tag (e.g. <c>gemma3:4b</c>),
/// as returned by the <c>/api/tags</c> endpoint.
/// </summary>
public sealed record OllamaModelInfo(string Id, long SizeBytes, string? Digest, DateTimeOffset? ModifiedAt);
