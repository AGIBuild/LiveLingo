using System.Net;
using System.Net.Http.Headers;

namespace LiveLingo.HfGguf;

/// <summary>
/// Downloads via <c>/{repo}/resolve/{rev}/{path}</c> with Range resume and a sidecar <c>.part</c> file.
/// </summary>
public sealed class HfResolveDownloader(HttpClient http, string hubResolveBase = "https://huggingface.co") : IHfDownloader
{
    private readonly string _hubResolveBase = hubResolveBase.TrimEnd('/');

    public async Task DownloadAsync(
        string repoId,
        string revision,
        string filePath,
        string destinationFilePath,
        string? bearerToken,
        bool forceRestart,
        int bufferSize,
        IProgress<HfDownloadProgress>? progress,
        CancellationToken cancellationToken = default,
        string? hubResolveBaseOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        if (bufferSize < 4096)
            throw new ArgumentOutOfRangeException(nameof(bufferSize));

        var hubBase = (hubResolveBaseOverride ?? _hubResolveBase).TrimEnd('/');

        var dest = Path.GetFullPath(destinationFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var partPath = dest + ".part";
        if (File.Exists(dest) && !forceRestart)
            return;

        if (forceRestart && File.Exists(partPath))
            File.Delete(partPath);

        var resumeFrom = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;
        if (forceRestart && resumeFrom > 0)
        {
            File.Delete(partPath);
            resumeFrom = 0;
        }

        var encodedPath = string.Join('/', filePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var resolveUrl =
            $"{hubBase}/{repoId.Trim().Trim('/')}/resolve/{Uri.EscapeDataString(revision)}/{encodedPath}";

        var (response, appendOffset) = await GetResponseForResumeAsync(
                resolveUrl,
                repoId,
                filePath,
                partPath,
                resumeFrom,
                bearerToken,
                cancellationToken)
            .ConfigureAwait(false);

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new HfHubException(
                    $"Access denied for '{repoId}' file '{filePath}' (HTTP {(int)response.StatusCode}). Configure a Hugging Face token.",
                    (int)response.StatusCode,
                    resolveUrl);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HfHubException(
                    $"Download failed for '{repoId}' file '{filePath}': HTTP {(int)response.StatusCode}",
                    (int)response.StatusCode,
                    resolveUrl);
            }

            await CopyResponseToPartAsync(
                    response,
                    partPath,
                    dest,
                    repoId,
                    filePath,
                    resolveUrl,
                    appendOffset,
                    bufferSize,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<(HttpResponseMessage Response, long AppendOffset)> GetResponseForResumeAsync(
        string resolveUrl,
        string repoId,
        string filePath,
        string partPath,
        long resumeFrom,
        string? bearerToken,
        CancellationToken ct)
    {
        var response = await SendGetAsync(resolveUrl, resumeFrom, bearerToken, ct).ConfigureAwait(false);

        if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            response.Dispose();
            if (File.Exists(partPath))
                File.Delete(partPath);
            response = await SendGetAsync(resolveUrl, 0, bearerToken, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                response.Dispose();
                throw new HfHubException(
                    $"Server returned HTTP {code} instead of partial content when resuming '{repoId}' file '{filePath}'. Partial file removed; retry full download or use --force.",
                    code,
                    resolveUrl);
            }

            return (response, 0);
        }

        return (response, resumeFrom);
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string url,
        long resumeFrom,
        string? bearerToken,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());

        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task CopyResponseToPartAsync(
        HttpResponseMessage response,
        string partPath,
        string finalPath,
        string repoId,
        string filePath,
        string resolveUrl,
        long appendOffset,
        int bufferSize,
        IProgress<HfDownloadProgress>? progress,
        CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        if (response.Content.Headers.ContentRange?.Length is { } len)
            total = len;

        var mode = appendOffset > 0 ? FileMode.Append : FileMode.Create;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (var fileStream = new FileStream(
                           partPath,
                           mode,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize,
                           FileOptions.Asynchronous))
        {
            var buffer = new byte[bufferSize];
            var written = appendOffset;
            int read;
            while ((read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;
                progress?.Report(new HfDownloadProgress(written, total));
            }

            await fileStream.FlushAsync(ct).ConfigureAwait(false);

            if (total is > 0 && written != total)
            {
                throw new HfHubException(
                    $"Size mismatch for '{repoId}' '{filePath}': expected {total} bytes, got {written}.",
                    (int)response.StatusCode,
                    resolveUrl);
            }
        }

        File.Move(partPath, finalPath, overwrite: true);
    }
}
