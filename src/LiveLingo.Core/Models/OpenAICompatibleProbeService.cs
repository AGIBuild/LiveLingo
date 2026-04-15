using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace LiveLingo.Core.Models;

public sealed class OpenAICompatibleProbeService(
    HttpClient http,
    ILogger<OpenAICompatibleProbeService> logger) : ICloudProviderProbeService
{
    public async Task<CloudProviderConnectionResult> TestConnectionAsync(
        CloudProviderProbeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var catalog = await GetModelCatalogAsync(request, ct).ConfigureAwait(false);
            if (catalog.IsSupported)
            {
                var count = catalog.Models.Count;
                var message = count > 0
                    ? $"Connection succeeded. {count} models available."
                    : "Provider model catalog returned no models.";
                return new CloudProviderConnectionResult(count > 0, message, count);
            }

            if (string.IsNullOrWhiteSpace(request.TranslationModelId))
            {
                return new CloudProviderConnectionResult(
                    false,
                    "Provider does not expose a model catalog. Configure a translation model to run a direct connection test.");
            }

            await ProbeModelAsync(request, request.TranslationModelId, ct).ConfigureAwait(false);
            return new CloudProviderConnectionResult(
                true,
                $"Connection succeeded. Provider does not expose a model catalog; validated translation model '{request.TranslationModelId}'.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException or TimeoutRejectedException)
        {
            logger.LogWarning(ex, "Cloud provider probe failed for {BaseUrl}", request.BaseUrl);
            return new CloudProviderConnectionResult(false, ex.Message);
        }
    }

    public async Task<CloudProviderModelCatalogResult> GetModelCatalogAsync(
        CloudProviderProbeRequest request,
        CancellationToken ct = default)
    {
        var baseUrl = request.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Cloud provider base URL is required.");

        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Cloud provider API key is required.");

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            OpenAICompatibleEndpoints.BuildModelsEndpoint(baseUrl));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented)
            return new CloudProviderModelCatalogResult(false, []);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return new CloudProviderModelCatalogResult(true, []);

        var models = data.EnumerateArray()
            .Select(model => new CloudProviderModelInfo(
                model.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? string.Empty : string.Empty,
                model.TryGetProperty("owned_by", out var ownerNode) ? ownerNode.GetString() : null))
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CloudProviderModelCatalogResult(true, models);
    }

    public async Task ProbeModelAsync(
        CloudProviderProbeRequest request,
        string modelId,
        CancellationToken ct = default)
    {
        var baseUrl = request.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Cloud provider base URL is required.");

        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Cloud provider API key is required.");

        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("Cloud provider model id is required.");

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            OpenAICompatibleEndpoints.BuildChatCompletionsEndpoint(baseUrl));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(new ProbeChatRequest(
            modelId.Trim(),
            [new ProbeChatMessage("system", "Reply with OK."), new ProbeChatMessage("user", "ping")],
            1,
            false));

        using var response = await http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private sealed record ProbeChatRequest(
        string model,
        IReadOnlyList<ProbeChatMessage> messages,
        int max_tokens,
        bool stream);

    private sealed record ProbeChatMessage(string role, string content);
}
