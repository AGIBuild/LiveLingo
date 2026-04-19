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

    /// <summary>
    /// Removes residue of model ids no longer registered in <see cref="ModelRegistry"/>
    /// (typically supplied from <see cref="ObsoleteModelRegistry.Ids"/>). Returns the total
    /// bytes reclaimed; safe to call on every startup — missing directories are no-ops.
    /// </summary>
    Task<long> CleanObsoleteModelsAsync(
        IEnumerable<string> obsoleteModelIds,
        CancellationToken ct = default);
}
