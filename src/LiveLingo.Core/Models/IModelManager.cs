namespace LiveLingo.Core.Models;

public interface IModelManager
{
    Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    IReadOnlyList<InstalledModel> ListInstalled();

    /// <summary>
    /// True when every file required by the current <see cref="ModelDescriptor"/> exists under the model directory.
    /// </summary>
    bool HasAllExpectedLocalAssets(ModelDescriptor descriptor);

    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    long GetTotalDiskUsage();
    string GetModelDirectory(string modelId);
    Task MigrateStoragePathAsync(string newPath, CancellationToken ct = default);

    /// <summary>
    /// Clears automatic hf-mirror fallback so the next Hugging Face download tries the primary hub again.
    /// </summary>
    void ResetHuggingfaceTransportFallback();
}
