using LiveLingo.HfGguf;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Wraps <see cref="HfResolveDownloader"/> with LiveLingo-specific concerns:
/// the active mirror policy, the user's bearer token, and the auto fallback retry
/// when the canonical hub is unreachable from this machine.
/// </summary>
internal sealed class HfResolveAssetDownloader
{
    private const int BufferSize = 1024 * 1024;

    private readonly CoreOptions _options;
    private readonly HfResolveDownloader _downloader;
    private readonly HuggingFaceMirrorPolicy _mirror;
    private readonly ILogger _logger;

    public HfResolveAssetDownloader(
        CoreOptions options,
        HfResolveDownloader downloader,
        HuggingFaceMirrorPolicy mirror,
        ILogger logger)
    {
        _options = options;
        _downloader = downloader;
        _mirror = mirror;
        _logger = logger;
    }

    public async Task<long> DownloadAsync(
        string modelId,
        string relativePath,
        string finalPath,
        ModelAsset asset,
        long downloadedBeforeAsset,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        if (!HuggingFaceResolveUrl.TryParse(asset.DownloadUrl, out var repoId, out var revision, out var filePath))
            throw new InvalidOperationException("Expected a Hugging Face /resolve/ URL.");

        var token = string.IsNullOrWhiteSpace(_options.HuggingFaceToken) ? null : _options.HuggingFaceToken!.Trim();
        var hfProgress = new Progress<HfDownloadProgress>(p =>
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + p.DownloadedBytes, totalBytes)));

        var hub = _mirror.GetEffectiveHubBase(asset.DownloadUrl);
        try
        {
            try
            {
                await _downloader
                    .DownloadAsync(repoId!, revision, filePath!, finalPath, token, forceRestart: false, BufferSize, hfProgress, ct, hubResolveBaseOverride: hub)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (_mirror.ShouldFallbackToMirror(asset.DownloadUrl, ex))
            {
                _logger.LogWarning(ex,
                    "HF asset download unreachable via primary hub, retrying fallback mirror for model={ModelId} asset={AssetPath}",
                    modelId,
                    relativePath);
                _mirror.EngageFallbackMirror();
                await _downloader
                    .DownloadAsync(repoId!, revision, filePath!, finalPath, token, forceRestart: false, BufferSize, hfProgress, ct, hubResolveBaseOverride: HuggingFaceMirrorPolicy.DefaultFallbackMirror)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("Completed HF resolve download: model={ModelId}, asset={AssetPath}", modelId, relativePath);
            var len = new FileInfo(finalPath).Length;
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + len, totalBytes));
            return len;
        }
        catch (HfHubException ex) when (ex.StatusCode is 401 or 403)
        {
            throw new ModelDownloadAuthorizationException(
                "Hugging Face rejected this download. Create a read token at huggingface.co/settings/tokens, paste it under Settings → Advanced → Access token, then click Save.",
                ex);
        }
    }
}
