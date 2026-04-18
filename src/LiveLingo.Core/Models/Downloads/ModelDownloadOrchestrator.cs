using LiveLingo.Core.Models.Installations;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Drives the "ensure this model is fully on disk" workflow:
/// detects the install/repair branch, sums asset sizes for the progress total,
/// reserves disk space, fans each asset out to <see cref="ModelAssetDownloader"/>,
/// then writes the manifest. Concurrent ensures for the same model id share one task
/// via <see cref="InflightDownloadRegistry"/>.
/// </summary>
internal sealed class ModelDownloadOrchestrator
{
    private readonly CoreOptions _options;
    private readonly ModelAssetDownloader _assetDownloader;
    private readonly InflightDownloadRegistry _inflight;
    private readonly ILogger _logger;

    public ModelDownloadOrchestrator(
        CoreOptions options,
        ModelAssetDownloader assetDownloader,
        InflightDownloadRegistry inflight,
        ILogger logger)
    {
        _options = options;
        _assetDownloader = assetDownloader;
        _inflight = inflight;
        _logger = logger;
    }

    public Task EnsureAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        var modelDir = ModelStoragePaths.GetModelDirectory(_options.ModelStoragePath, descriptor.Id);
        var manifestPath = ModelStoragePaths.GetManifestPath(modelDir);

        if (File.Exists(manifestPath))
        {
            var missingAssets = ModelStoragePaths.GetExpectedAssets(descriptor)
                .Where(asset => !File.Exists(Path.Combine(modelDir, ModelStoragePaths.NormalizeRelativePath(asset.RelativePath))))
                .ToArray();

            if (missingAssets.Length == 0)
            {
                _logger.LogDebug("Model {Id} already installed at {Path}", descriptor.Id, modelDir);
                return Task.CompletedTask;
            }

            _logger.LogInformation(
                "Model {Id} is installed but missing {MissingCount} assets. Repairing installation.",
                descriptor.Id,
                missingAssets.Length);

            return _inflight.GetOrAdd(descriptor.Id, _ =>
                DownloadMissingAssetsAsync(descriptor, modelDir, manifestPath, missingAssets, progress, ct));
        }

        return _inflight.GetOrAdd(descriptor.Id, _ =>
            DownloadModelAsync(descriptor, modelDir, manifestPath, progress, ct));
    }

    private async Task DownloadModelAsync(
        ModelDescriptor descriptor,
        string modelDir,
        string manifestPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(modelDir);
            DiskSpaceGuard.EnsureAvailable(modelDir, descriptor.SizeBytes);

            var assets = ModelStoragePaths.GetExpectedAssets(descriptor);
            var totalBytes = assets.Sum(a => a.SizeBytes > 0 ? a.SizeBytes : 0);
            if (totalBytes <= 0)
                totalBytes = descriptor.SizeBytes;

            _logger.LogInformation(
                "Starting model download {ModelId}: assetCount={AssetCount}, expectedBytes={TotalBytes}, targetDir={ModelDir}",
                descriptor.Id,
                assets.Count,
                totalBytes,
                modelDir);

            await DownloadAssetSequenceAsync(descriptor.Id, modelDir, assets, totalBytes, progress, ct).ConfigureAwait(false);

            await WriteManifestAsync(descriptor, manifestPath, ct).ConfigureAwait(false);
            _logger.LogDebug("Model {Id} downloaded to {Path}", descriptor.Id, modelDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download failed for {ModelId}", descriptor.Id);
            throw;
        }
        finally
        {
            _inflight.Release(descriptor.Id);
        }
    }

    private async Task DownloadMissingAssetsAsync(
        ModelDescriptor descriptor,
        string modelDir,
        string manifestPath,
        IReadOnlyList<ModelAsset> missingAssets,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(modelDir);

            var expectedBytes = missingAssets.Sum(a => a.SizeBytes > 0 ? a.SizeBytes : 0);
            if (expectedBytes > 0)
                DiskSpaceGuard.EnsureAvailable(modelDir, expectedBytes);

            var totalBytes = expectedBytes > 0 ? expectedBytes : descriptor.SizeBytes;
            _logger.LogInformation(
                "Repairing model assets for {ModelId}: missingCount={MissingCount}, expectedBytes={ExpectedBytes}",
                descriptor.Id,
                missingAssets.Count,
                totalBytes);

            await DownloadAssetSequenceAsync(descriptor.Id, modelDir, missingAssets, totalBytes, progress, ct).ConfigureAwait(false);

            await WriteManifestAsync(descriptor, manifestPath, ct).ConfigureAwait(false);
            _logger.LogDebug("Model {Id} assets repaired at {Path}", descriptor.Id, modelDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model asset repair failed for {ModelId}", descriptor.Id);
            throw;
        }
        finally
        {
            _inflight.Release(descriptor.Id);
        }
    }

    private async Task DownloadAssetSequenceAsync(
        string modelId,
        string modelDir,
        IReadOnlyList<ModelAsset> assets,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        long downloadedBytes = 0;
        foreach (var asset in assets)
        {
            downloadedBytes += await _assetDownloader
                .DownloadAsync(modelId, modelDir, asset, downloadedBytes, totalBytes, progress, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteManifestAsync(ModelDescriptor descriptor, string manifestPath, CancellationToken ct)
    {
        var manifest = ModelManifest.FromDescriptor(descriptor);
        await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), ct).ConfigureAwait(false);
    }
}
