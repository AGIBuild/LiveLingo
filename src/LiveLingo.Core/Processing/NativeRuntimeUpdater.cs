using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;

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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) os = "macos";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) os = "win";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) os = "ubuntu"; // llama.cpp uses ubuntu

            var searchString = $"{os}-{arch}";
            // Fallback for macos-arm64
            if (os == "macos" && arch == "x64") searchString = "macos-x64"; // maybe?

            // Actually, let's just get the latest release
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LiveLingo/1.0");
            var releaseUrl = "https://api.github.com/repos/ggerganov/llama.cpp/releases/latest";
            
            var release = await http.GetFromJsonAsync<JsonElement>(releaseUrl, ct);
            var tagName = release.GetProperty("tag_name").GetString();
            
            var nativeDir = Path.Combine(options.Value.ModelStoragePath, "native", tagName!);
            if (Directory.Exists(nativeDir) && Directory.GetFiles(nativeDir, "*llama*").Length > 0)
            {
                LlamaNativeBootstrap.ApplySearchPathOverrides(nativeDir, null);
                return;
            }

            string? downloadUrl = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.Contains(searchString, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip"))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (downloadUrl == null)
            {
                logger.LogWarning("Could not find native llama.cpp release for {Platform}", searchString);
                return;
            }

            logger.LogInformation("Downloading newer llama.cpp runtime: {Url}", downloadUrl);
            Directory.CreateDirectory(nativeDir);
            
            var zipPath = Path.Combine(nativeDir, "temp.zip");
            var bytes = await http.GetByteArrayAsync(downloadUrl, ct);
            await File.WriteAllBytesAsync(zipPath, bytes, ct);
            
            ZipFile.ExtractToDirectory(zipPath, nativeDir, true);
            File.Delete(zipPath);
            
            // The zip extracts to a subfolder maybe, or directly. Find the library folder.
            var libDir = nativeDir;
            var binBuildDir = Path.Combine(nativeDir, "build", "bin");
            if (Directory.Exists(binBuildDir))
                libDir = binBuildDir;

            LlamaNativeBootstrap.ApplySearchPathOverrides(libDir, null);
            logger.LogInformation("Native runtime updated and injected from {Path}", libDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update native runtime");
        }
    }
}
