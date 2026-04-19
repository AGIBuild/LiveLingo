using LiveLingo.Core.Models.Downloads;
using LiveLingo.Core.Models.Installations;
using LiveLingo.HfGguf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

/// <summary>
/// Public entry point for model lifecycle: download/repair, install enumeration,
/// disk usage, deletion, storage relocation, and the HF mirror fallback latch.
///
/// All real work is delegated to single-purpose collaborators in
/// <see cref="LiveLingo.Core.Models.Downloads"/> and
/// <see cref="LiveLingo.Core.Models.Installations"/>; this class just wires
/// the dependency graph from the IOptions/HttpClient/ILogger triple that the
/// DI container supplies.
/// </summary>
public sealed class ModelManager : IModelManager
{
    private readonly HuggingFaceMirrorPolicy _mirrorPolicy;
    private readonly ModelDownloadOrchestrator _orchestrator;
    private readonly InstalledModelStore _installedStore;
    private readonly ModelStoragePathMigrator _pathMigrator;
    private readonly ObsoleteModelCleaner _obsoleteCleaner;

    public ModelManager(IOptions<CoreOptions> options, HttpClient http, ILogger<ModelManager> logger)
    {
        var opts = options.Value;

        _mirrorPolicy = new HuggingFaceMirrorPolicy(opts, logger);
        var hfRawDownloader = new HfResolveDownloader(http);
        var httpRangeDownloader = new HttpRangeDownloader(http, _mirrorPolicy, logger);
        var hfAssetDownloader = new HfResolveAssetDownloader(opts, hfRawDownloader, _mirrorPolicy, logger);
        var assetDownloader = new ModelAssetDownloader(httpRangeDownloader, hfAssetDownloader, logger);
        var inflight = new InflightDownloadRegistry();

        _orchestrator = new ModelDownloadOrchestrator(opts, assetDownloader, inflight, logger);
        _installedStore = new InstalledModelStore(opts, logger);
        _pathMigrator = new ModelStoragePathMigrator(opts, logger);
        _obsoleteCleaner = new ObsoleteModelCleaner(opts, logger);
    }

    public Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default) =>
        _orchestrator.EnsureAsync(descriptor, progress, ct);

    public IReadOnlyList<InstalledModel> ListInstalled() => _installedStore.List();

    public bool HasAllExpectedLocalAssets(ModelDescriptor descriptor) =>
        _installedStore.HasAllExpectedLocalAssets(descriptor);

    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) =>
        _installedStore.DeleteAsync(modelId, ct);

    public long GetTotalDiskUsage() => _installedStore.GetTotalDiskUsage();

    public string GetModelDirectory(string modelId) => _installedStore.GetModelDirectory(modelId);

    public Task MigrateStoragePathAsync(string newPath, CancellationToken ct = default) =>
        _pathMigrator.MigrateAsync(newPath, ct);

    public void ResetHuggingfaceTransportFallback() => _mirrorPolicy.Reset();

    public Task<long> CleanObsoleteModelsAsync(
        IEnumerable<string> obsoleteModelIds,
        CancellationToken ct = default) =>
        _obsoleteCleaner.CleanAsync(obsoleteModelIds, ct);
}
