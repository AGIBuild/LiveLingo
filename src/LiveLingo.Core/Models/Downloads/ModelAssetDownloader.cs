using System.Security.Cryptography;
using LiveLingo.Core.Models.Installations;
using LiveLingo.HfGguf;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Routes a single asset download to the right transport — Hugging Face's
/// resolve API for HF URLs, plain HTTP Range otherwise — and short-circuits
/// when the file (or a complete <c>.part</c>) is already on disk. A freshly downloaded file is verified
/// against its pinned SHA256 and deleted when it does not match.
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

        var downloadedBytes = HuggingFaceResolveUrl.TryParse(asset.DownloadUrl, out _, out _, out _)
            ? await _hfDownloader
                .DownloadAsync(modelId, relativePath, finalPath, asset, downloadedBeforeAsset, totalBytes, progress, ct)
                .ConfigureAwait(false)
            : await _httpDownloader
                .DownloadAsync(modelId, relativePath, finalPath, asset, downloadedBeforeAsset, totalBytes, progress, ct)
                .ConfigureAwait(false);

        await VerifyAsync(modelId, relativePath, finalPath, asset.Sha256, ct).ConfigureAwait(false);
        return downloadedBytes;
    }

    private async Task VerifyAsync(string modelId, string relativePath, string path, string expected, CancellationToken ct)
    {
        string actual;
        await using (var file = File.OpenRead(path))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            return;

        File.Delete(path);
        _logger.LogError(
            "Integrity check failed: model={ModelId}, asset={AssetPath}, expected={Expected}, actual={Actual}",
            modelId, relativePath, expected, actual);
        throw new ModelIntegrityException(modelId, relativePath, expected, actual);
    }
}
