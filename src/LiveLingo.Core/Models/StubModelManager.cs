namespace LiveLingo.Core.Models;

public sealed class StubModelManager : IModelManager
{
    public Task EnsureModelAsync(
        ModelDescriptor descriptor, IProgress<ModelDownloadProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public IReadOnlyList<InstalledModel> ListInstalled() => [];
    public Task DeleteModelAsync(string modelId, CancellationToken ct) => Task.CompletedTask;
    public long GetTotalDiskUsage() => 0;
}
