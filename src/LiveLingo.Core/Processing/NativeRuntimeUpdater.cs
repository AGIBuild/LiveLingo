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
    Task EnsureLatestNativeRuntimeAsync(CancellationToken ct = default);
}

public class NativeRuntimeUpdater(
    HttpClient http,
    IOptions<CoreOptions> options,
    ILogger<NativeRuntimeUpdater> logger) : INativeRuntimeUpdater
{
    public async Task EnsureLatestNativeRuntimeAsync(CancellationToken ct = default)
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
                os = "win-cpu"; // LlamaSharp fallback assumes CPU
                ext = "zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = "ubuntu";
            }

            // Fallback for macos-arm64
            if (os == "macos" && arch == "x64") os = "macos"; // it's macos-x64.tar.gz

            string tagName;
            try
            {
                // Bypassing GitHub API rate limits by following the redirect of the latest release page
                var request = new HttpRequestMessage(HttpMethod.Head, "https://github.com/ggml-org/llama.cpp/releases/latest");
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
                
                var match = Regex.Match(finalUrl, @"tag/([^/]+)");
                if (!match.Success)
                {
                    logger.LogWarning("Could not determine latest llama.cpp release tag from redirect URL: {Url}", finalUrl);
                    return;
                }
                tagName = match.Groups[1].Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve latest llama.cpp release tag.");
                return;
            }

            var nativeDir = Path.Combine(options.Value.ModelStoragePath, "native", tagName);
            if (Directory.Exists(nativeDir) && Directory.GetFiles(nativeDir, "*llama*", SearchOption.AllDirectories).Length > 0)
            {
                LlamaNativeBootstrap.ApplySearchPathOverrides(GetLibDir(nativeDir), null);
                return;
            }

            var assetName = $"llama-{tagName}-bin-{os}-{arch}.{ext}";
            var downloadUrl = $"https://github.com/ggml-org/llama.cpp/releases/download/{tagName}/{assetName}";

            logger.LogInformation("Downloading newer llama.cpp runtime: {Url}", downloadUrl);
            Directory.CreateDirectory(nativeDir);
            
            var archivePath = Path.Combine(nativeDir, $"temp.{ext}");
            var bytes = await http.GetByteArrayAsync(downloadUrl, ct);
            await File.WriteAllBytesAsync(archivePath, bytes, ct);
            
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
            
            var libDir = GetLibDir(nativeDir);
            LlamaNativeBootstrap.ApplySearchPathOverrides(libDir, null);
            logger.LogInformation("Native runtime updated and injected from {Path}", libDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update native runtime");
        }
    }

    private static string GetLibDir(string nativeDir)
    {
        var libFiles = Directory.GetFiles(nativeDir, "*llama*", SearchOption.AllDirectories);
        foreach (var file in libFiles)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".dll" || ext == ".dylib" || ext == ".so")
            {
                return Path.GetDirectoryName(file) ?? nativeDir;
            }
        }
        return nativeDir;
    }
}
