using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Formats.Tar;

namespace LiveLingo.Core.Processing;

public interface INativeRuntimeUpdater
{
    Task<string?> EnsureLatestLlamaServerAsync(CancellationToken ct = default);
}

public class NativeRuntimeUpdater(
    HttpClient http,
    IOptions<CoreOptions> options,
    ILogger<NativeRuntimeUpdater> logger) : INativeRuntimeUpdater
{
    public async Task<string?> EnsureLatestLlamaServerAsync(CancellationToken ct = default)
    {
        try
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                _ => throw new NotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
            };

            var os = "unknown";
            var ext = "tar.gz";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                os = "macos";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                os = "win";
                ext = "zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = "ubuntu";
            }

            // Fallback for macos-arm64
            if (os == "macos" && arch == "x64") os = "macos"; // it's macos-x64.tar.gz
            if (os == "win") os = "win-cpu"; // try win-cpu for best compatibility first, or win-vulkan etc. if desired

            string tagName;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, "https://github.com/ggml-org/llama.cpp/releases/latest");
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var match = TryMatchReleaseTag(response.RequestMessage?.RequestUri, response.Headers.Location);
                if (!match.Success)
                {
                    var requestUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                    var locationUrl = response.Headers.Location?.ToString() ?? "";
                    logger.LogWarning(
                        "Could not determine latest llama.cpp release tag from redirect response. RequestUri={RequestUrl}, Location={LocationUrl}",
                        requestUrl,
                        locationUrl);
                    return null;
                }
                tagName = match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve latest llama.cpp release tag.");
                return null;
            }

            var nativeDir = Path.Combine(options.Value.ModelStoragePath, "native", tagName);
            var serverExecutable = FindServerExecutable(nativeDir);
            
            if (serverExecutable is not null)
            {
                return serverExecutable;
            }

            var assetName = $"llama-{tagName}-bin-{os}-{arch}.{ext}";
            var downloadUrl = $"https://github.com/ggml-org/llama.cpp/releases/download/{tagName}/{assetName}";

            logger.LogInformation("Downloading newer llama.cpp runtime: {Url}", downloadUrl);
            Directory.CreateDirectory(nativeDir);
            
            // Use a stable name so we can resume partial downloads on retry.
            var archivePath = Path.Combine(nativeDir, assetName);
            try
            {
                await DownloadWithResumeAsync(http, downloadUrl, archivePath, logger, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Failed to download llama-server from {Url}", downloadUrl);
                return null;
            }
            
            if (ext == "zip")
            {
                ZipFile.ExtractToDirectory(archivePath, nativeDir, true);
            }
            else if (ext == "tar.gz")
            {
                await using var fileStream = File.OpenRead(archivePath);
                await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzipStream, nativeDir, overwriteFiles: true, cancellationToken: ct);
            }

            try
            {
                File.Delete(archivePath);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
            
            serverExecutable = FindServerExecutable(nativeDir);
            
            if (serverExecutable is not null)
            {
                // Ensure executable permissions on Unix
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    File.SetUnixFileMode(serverExecutable, File.GetUnixFileMode(serverExecutable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
                logger.LogInformation("llama-server updated and ready at {Path}", serverExecutable);
                return serverExecutable;
            }
            
            logger.LogWarning("Could not find llama-server executable in the downloaded archive.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update native runtime");
            return null;
        }
    }

    private static async Task DownloadWithResumeAsync(
        HttpClient http,
        string url,
        string destinationPath,
        ILogger logger,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var existingLength = 0L;
                if (File.Exists(destinationPath))
                {
                    try { existingLength = new FileInfo(destinationPath).Length; }
                    catch (IOException) { existingLength = 0; }
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existingLength > 0)
                    request.Headers.Range = new RangeHeaderValue(existingLength, null);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var append = response.StatusCode == System.Net.HttpStatusCode.PartialContent && existingLength > 0;

                await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var output = new FileStream(
                    destinationPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 1024 * 128,
                    useAsync: true);

                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);

                // Best-effort validation for ranged downloads.
                var contentRange = response.Content.Headers.ContentRange;
                if (contentRange is { HasLength: true })
                {
                    var expectedTotal = contentRange.Length ?? 0;
                    if (expectedTotal > 0)
                    {
                        var finalLen = 0L;
                        try { finalLen = new FileInfo(destinationPath).Length; } catch (IOException) { }
                        if (finalLen != expectedTotal)
                            throw new IOException($"Partial download incomplete (bytes={finalLen}, expected={expectedTotal}).");
                    }
                }

                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (attempt == maxAttempts)
                    throw;

                logger.LogWarning(ex, "Download attempt {Attempt}/{MaxAttempts} failed; retrying in {Delay}.", attempt, maxAttempts, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 8000));
            }
        }
    }

    private static Match TryMatchReleaseTag(Uri? requestUri, Uri? locationHeader)
    {
        var candidates = new List<string>();

        if (requestUri is not null)
            candidates.Add(requestUri.ToString());

        if (locationHeader is not null)
        {
            candidates.Add(locationHeader.ToString());

            if (!locationHeader.IsAbsoluteUri && requestUri is not null)
                candidates.Add(new Uri(requestUri, locationHeader).ToString());
        }

        foreach (var candidate in candidates)
        {
            var match = Regex.Match(candidate, @"tag/([^/]+)");
            if (match.Success)
                return match;
        }

        return Regex.Match(string.Empty, @"tag/([^/]+)");
    }

    private static string? FindServerExecutable(string nativeDir)
    {
        if (!Directory.Exists(nativeDir)) return null;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama-server.exe" : "llama-server";
        var files = Directory.GetFiles(nativeDir, exeName, SearchOption.AllDirectories);
        
        return files.FirstOrDefault();
    }
}
