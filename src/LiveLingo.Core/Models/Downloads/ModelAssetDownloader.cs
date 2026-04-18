using LiveLingo.Core.Models.Installations;
using LiveLingo.HfGguf;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Routes a single asset download to the right transport — Hugging Face's
/// resolve API for HF URLs, plain HTTP Range otherwise — and short-circuits
/// when the file (or a complete <c>.part</c>) is already on disk.
/// </summary>
internal sealed class ModelAssetDownloader
{
    private readonly HttpRangeDownloader _httpDownloader;
    private readonly HfResolveAssetDownloader _hfDownloader;
    private readonly ILogger _logger;

    public ModelAssetDownloader(
        HttpRangeDownloader httpDownloader,
        HfResolveAssetDownloader hfDownloader,
        ILogger logger)
    {
        _httpDownloader = httpDownloader;
        _hfDownloader = hfDownloader;
        _logger = logger;
    }

    public async Task<long> DownloadAsync(
        string modelId,
        string modelDir,
        ModelAsset asset,
        long downloadedBeforeAsset,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        var relativePath = ModelStoragePaths.NormalizeRelativePath(asset.RelativePath);
        var finalPath = Path.Combine(modelDir, relativePath);
        var parent = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        if (File.Exists(finalPath))
        {
            var existingFileSize = new FileInfo(finalPath).Length;
            _logger.LogDebug(
                "Model asset already exists: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
                modelId,
                relativePath,
                existingFileSize);
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + existingFileSize, totalBytes));
            return existingFileSize;
        }

        if (HuggingFaceResolveUrl.TryParse(asset.DownloadUrl, out _, out _, out _))
        {
            return await _hfDownloader
                .DownloadAsync(modelId, relativePath, finalPath, asset, downloadedBeforeAsset, totalBytes, progress, ct)
                .ConfigureAwait(false);
        }

        return await _httpDownloader
            .DownloadAsync(modelId, relativePath, finalPath, asset, downloadedBeforeAsset, totalBytes, progress, ct)
            .ConfigureAwait(false);
    }
}
