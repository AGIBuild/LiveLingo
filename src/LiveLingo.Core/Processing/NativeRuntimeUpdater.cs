using System.Net.Http.Json;
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
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                
                var match = Regex.Match(finalUrl, @"tag/([^/]+)");
                if (!match.Success)
                {
                    logger.LogWarning("Could not determine latest llama.cpp release tag from redirect URL: {Url}", finalUrl);
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
            
            var archivePath = Path.Combine(nativeDir, $"temp.{ext}");
            try
            {
                var bytes = await http.GetByteArrayAsync(downloadUrl, ct);
                await File.WriteAllBytesAsync(archivePath, bytes, ct);
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

            File.Delete(archivePath);
            
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

    private static string? FindServerExecutable(string nativeDir)
    {
        if (!Directory.Exists(nativeDir)) return null;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama-server.exe" : "llama-server";
        var files = Directory.GetFiles(nativeDir, exeName, SearchOption.AllDirectories);
        
        return files.FirstOrDefault();
    }
}