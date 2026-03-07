namespace LiveLingo.Core.Models;

public interface IModelManager
{
    Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    IReadOnlyList<InstalledModel> ListInstalled();
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    long GetTotalDiskUsage();
    string GetModelDirectory(string modelId);
    Task MigrateStoragePathAsync(string newPath, CancellationToken ct = default);
}
