using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Streams a single asset to disk using HTTP Range requests, with <c>.part</c>-file
/// resume semantics and 416 (range-not-satisfiable) recovery. The HuggingFace mirror
/// policy is consulted up-front to rewrite the URL and to attach a bearer token.
/// </summary>
internal sealed class HttpRangeDownloader
{
    private const int CopyBufferSize = 81920;

    private readonly HttpClient _http;
    private readonly HuggingFaceMirrorPolicy _mirror;
    private readonly ILogger _logger;

    public HttpRangeDownloader(HttpClient http, HuggingFaceMirrorPolicy mirror, ILogger logger)
    {
        _http = http;
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
        var partPath = finalPath + ".part";
        var existingPartBytes = await ReconcileExistingPartAsync(modelId, relativePath, finalPath, partPath, asset, downloadedBeforeAsset, totalBytes, progress);
        if (existingPartBytes is { } completed)
            return completed;

        existingPartBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        _logger.LogInformation(
            "Downloading model asset: model={ModelId}, asset={AssetPath}, resumeBytes={ResumeBytes}, url={Url}",
            modelId,
            relativePath,
            existingPartBytes!.Value,
            asset.DownloadUrl);

        var response = await SendAssetRequestAsync(asset.DownloadUrl, existingPartBytes!.Value, ct).ConfigureAwait(false);

        if (existingPartBytes.Value > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            var promoted = await TryPromotePartOn416Async(modelId, relativePath, finalPath, partPath, asset, response, downloadedBeforeAsset, totalBytes, existingPartBytes.Value, progress);
            if (promoted is { } promotedBytes)
                return promotedBytes;

            existingPartBytes = 0;
            response = await SendAssetRequestAsync(asset.DownloadUrl, 0, ct).ConfigureAwait(false);
        }

        using (response)
        {
            EnsureNotUnauthorized(response);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation(
                "Model asset response: model={ModelId}, asset={AssetPath}, statusCode={StatusCode}",
                modelId,
                relativePath,
                (int)response.StatusCode);

            if (existingPartBytes.Value > 0 && response.StatusCode == HttpStatusCode.OK)
            {
                File.Delete(partPath);
                existingPartBytes = 0;
            }

            return await CopyResponseToPartFileAsync(modelId, relativePath, finalPath, partPath, response, downloadedBeforeAsset, totalBytes, existingPartBytes!.Value, progress, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Inspects an existing <c>.part</c> file before any network traffic and returns the
    /// final byte count if the part is complete (or oversized and discarded). Returning
    /// <c>null</c> means the caller still needs to issue a network request.
    /// </summary>
    private async Task<long?> ReconcileExistingPartAsync(
        string modelId,
        string relativePath,
        string finalPath,
        string partPath,
        ModelAsset asset,
        long downloadedBeforeAsset,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress)
    {
        if (!File.Exists(partPath))
            return null;

        var existingPartBytes = new FileInfo(partPath).Length;

        if (asset.SizeBytes > 0 && existingPartBytes > asset.SizeBytes)
        {
            _logger.LogWarning(
                "Discarding oversized part file: model={ModelId}, asset={AssetPath}, partBytes={PartBytes}, expectedBytes={ExpectedBytes}",
                modelId,
                relativePath,
                existingPartBytes,
                asset.SizeBytes);
            File.Delete(partPath);
            return null;
        }

        if (asset.SizeBytes > 0 && existingPartBytes == asset.SizeBytes)
        {
            File.Move(partPath, finalPath, overwrite: true);
            _logger.LogInformation(
                "Promoted completed part file without network request: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
                modelId,
                relativePath,
                existingPartBytes);
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + existingPartBytes, totalBytes));
            return await Task.FromResult(existingPartBytes);
        }

        return null;
    }

    private async Task<long?> TryPromotePartOn416Async(
        string modelId,
        string relativePath,
        string finalPath,
        string partPath,
        ModelAsset asset,
        HttpResponseMessage response,
        long downloadedBeforeAsset,
        long totalBytes,
        long existingPartBytes,
        IProgress<ModelDownloadProgress>? progress)
    {
        var remoteLength = response.Content.Headers.ContentRange?.Length ?? asset.SizeBytes;
        response.Dispose();

        if (remoteLength > 0 && existingPartBytes == remoteLength)
        {
            File.Move(partPath, finalPath, overwrite: true);
            _logger.LogInformation(
                "Received 416 but part file already complete: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
                modelId,
                relativePath,
                existingPartBytes);
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + existingPartBytes, totalBytes));
            return await Task.FromResult(existingPartBytes);
        }

        _logger.LogWarning(
            "Received 416 for ranged request; restarting full download: model={ModelId}, asset={AssetPath}, resumeBytes={ResumeBytes}",
            modelId,
            relativePath,
            existingPartBytes);
        if (File.Exists(partPath))
            File.Delete(partPath);
        return null;
    }

    private async Task<long> CopyResponseToPartFileAsync(
        string modelId,
        string relativePath,
        string finalPath,
        string partPath,
        HttpResponseMessage response,
        long downloadedBeforeAsset,
        long totalBytes,
        long existingPartBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        long downloadedForAsset = existingPartBytes;
        await using (var fileStream = new FileStream(
                         partPath,
                         existingPartBytes > 0 ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            var buffer = new byte[CopyBufferSize];
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                downloadedForAsset += bytesRead;
                progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + downloadedForAsset, totalBytes));
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(partPath, finalPath, overwrite: true);
        _logger.LogInformation(
            "Completed model asset download: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
            modelId,
            relativePath,
            downloadedForAsset);
        return downloadedForAsset;
    }

    private static void EnsureNotUnauthorized(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ModelDownloadAuthorizationException(
                "Hugging Face rejected this download. Create a read token at huggingface.co/settings/tokens, paste it under Settings → Advanced → Access token, then click Save.");
        }
    }

    private async Task<HttpResponseMessage> SendAssetRequestAsync(string url, long resumeBytes, CancellationToken ct)
    {
        var effectiveUrl = _mirror.ApplyMirror(url);
        try
        {
            return await SendHttpGetAsync(effectiveUrl, resumeBytes, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (_mirror.ShouldFallbackToMirror(url, ex))
        {
            _logger.LogWarning(ex,
                "HuggingFace is unreachable, retrying with fallback mirror {Mirror}",
                HuggingFaceMirrorPolicy.DefaultFallbackMirror);
            _mirror.EngageFallbackMirror();
            var mirrorUrl = _mirror.RewriteToFallbackMirror(url);
            return await SendHttpGetAsync(mirrorUrl, resumeBytes, ct).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendHttpGetAsync(string url, long resumeBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeBytes > 0)
            request.Headers.Range = new RangeHeaderValue(resumeBytes, null);
        _mirror.AttachBearerIfAllowed(request, url);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }
}
