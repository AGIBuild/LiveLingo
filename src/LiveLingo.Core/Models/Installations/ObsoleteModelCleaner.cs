using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Installations;

/// <summary>
/// Removes on-disk residue of model ids that <see cref="ModelRegistry"/> no longer registers.
/// Idempotent — a missing directory is a no-op so this can run on every startup without
/// keeping any "already-cleaned" sentinel state.
/// </summary>
internal sealed class ObsoleteModelCleaner
{
    private readonly CoreOptions _options;
    private readonly ILogger _logger;

    public ObsoleteModelCleaner(CoreOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Deletes every model directory whose id appears in <paramref name="obsoleteModelIds"/>.
    /// Returns the total number of bytes reclaimed across all removed directories.
    /// </summary>
    public async Task<long> CleanAsync(
        IEnumerable<string> obsoleteModelIds,
        CancellationToken ct = default)
    {
        var storageRoot = _options.ModelStoragePath;
        if (string.IsNullOrWhiteSpace(storageRoot) || !Directory.Exists(storageRoot))
            return 0;

        long totalReleased = 0;
        foreach (var id in obsoleteModelIds)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var dir = ModelStoragePaths.GetModelDirectory(storageRoot, id);
            if (!Directory.Exists(dir))
                continue;

            long size;
            try
            {
                size = ComputeDirectorySize(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to size obsolete model '{Id}' at {Dir}; deleting anyway", id, dir);
                size = 0;
            }

            try
            {
                await Task.Run(() => Directory.Delete(dir, true), ct).ConfigureAwait(false);
                totalReleased += size;
                _logger.LogInformation(
                    "Removed obsolete model '{Id}' from {Dir} (freed {Bytes} bytes)",
                    id, dir, size);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete obsolete model '{Id}' at {Dir}", id, dir);
            }
        }

        return totalReleased;
    }

    private static long ComputeDirectorySize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
}
