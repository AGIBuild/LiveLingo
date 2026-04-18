using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace LiveLingo.Core.Models;

/// <summary>
/// Talks to the Ollama daemon's <c>/api/tags</c> endpoint directly (not via
/// OllamaSharp) so the probe stays decoupled from the chat provider's
/// HttpClient BaseAddress binding and can be invoked against arbitrary URLs
/// supplied by the settings UI before the user clicks "Save".
/// </summary>
public sealed class OllamaProbeService(
    HttpClient http,
    ILogger<OllamaProbeService> logger) : IOllamaProbeService
{
    public async Task<OllamaConnectionResult> TestConnectionAsync(
        OllamaProbeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var catalog = await GetModelCatalogAsync(request, ct).ConfigureAwait(false);
            var count = catalog.Models.Count;
            var message = count > 0
                ? $"Connection succeeded. {count} Ollama model{(count == 1 ? "" : "s")} available."
                : "Connected to Ollama, but no models have been pulled yet. Run 'ollama pull <model>' first.";
            return new OllamaConnectionResult(count > 0, message, count);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or HttpRequestException
                                      or JsonException
                                      or TaskCanceledException
                                      or TimeoutRejectedException)
        {
            logger.LogWarning(ex, "Ollama probe failed for {BaseUrl}", request.BaseUrl);
            return new OllamaConnectionResult(false, ex.Message);
        }
    }

    public async Task<OllamaModelCatalogResult> GetModelCatalogAsync(
        OllamaProbeRequest request,
        CancellationToken ct = default)
    {
        var baseUrl = request.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Ollama base URL is required.");

        var endpoint = BuildTagsEndpoint(baseUrl);
        using var response = await http.GetAsync(endpoint, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "Ollama did not respond at /api/tags. Verify the daemon is running ('ollama serve').");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            return new OllamaModelCatalogResult([]);
        }

        var result = models.EnumerateArray()
            .Select(static m => new OllamaModelInfo(
                m.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                m.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                m.TryGetProperty("digest", out var digest) ? digest.GetString() : null,
                m.TryGetProperty("modified_at", out var modified) &&
                    DateTimeOffset.TryParse(modified.GetString(), out var timestamp)
                        ? timestamp
                        : null))
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new OllamaModelCatalogResult(result);
    }

    private static string BuildTagsEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return $"{trimmed}/api/tags";
    }
}
