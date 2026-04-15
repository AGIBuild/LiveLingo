namespace LiveLingo.Core.Models;

public interface ICloudProviderProbeService
{
    Task<CloudProviderConnectionResult> TestConnectionAsync(
        CloudProviderProbeRequest request,
        CancellationToken ct = default);

    Task<CloudProviderModelCatalogResult> GetModelCatalogAsync(
        CloudProviderProbeRequest request,
        CancellationToken ct = default);

    Task ProbeModelAsync(
        CloudProviderProbeRequest request,
        string modelId,
        CancellationToken ct = default);
}

public sealed record CloudProviderProbeRequest(
    string BaseUrl,
    string ApiKey,
    string? TranslationModelId = null,
    string? PostProcessingModelId = null);

public sealed record CloudProviderConnectionResult(bool IsSuccess, string Message, int ModelCount = 0);

public sealed record CloudProviderModelCatalogResult(bool IsSupported, IReadOnlyList<CloudProviderModelInfo> Models);

public sealed record CloudProviderModelInfo(string Id, string? OwnedBy);
