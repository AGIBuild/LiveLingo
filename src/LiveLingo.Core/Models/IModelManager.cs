using System.ComponentModel;

namespace LiveLingo.Core.Models;

public interface IModelManager
{
    /// <summary>
    /// Low-level "fetch model bytes to disk" primitive. Prefer
    /// <see cref="IModelDownloadCoordinator.StartAsync"/> for any UI-visible
    /// download — the coordinator is the single source of truth for download
    /// progress / state across the Settings, Wizard, and Overlay surfaces, and
    /// it deduplicates concurrent calls. Direct callers of this method bypass
    /// that observability and should be limited to engines performing internal,
    /// non-user-facing on-demand model loads (where byte-level dedup is still
    /// provided by InflightDownloadRegistry).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
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
