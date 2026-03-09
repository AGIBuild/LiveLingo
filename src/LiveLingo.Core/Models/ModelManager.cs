using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

public sealed class ModelManager : IModelManager
{
    private readonly CoreOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<ModelManager> _logger;
    private readonly ConcurrentDictionary<string, Task> _inflight = new();

    public ModelManager(IOptions<CoreOptions> options, HttpClient http, ILogger<ModelManager> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
    }

    public async Task EnsureModelAsync(
        ModelDescriptor descriptor,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        var modelDir = GetModelDirectory(descriptor.Id);
        var manifestPath = Path.Combine(modelDir, "manifest.json");

        if (File.Exists(manifestPath))
        {
            var missingAssets = GetExpectedAssets(descriptor)
                .Where(asset => !File.Exists(Path.Combine(modelDir, NormalizeRelativePath(asset.RelativePath))))
                .ToArray();
            if (missingAssets.Length == 0)
            {
                _logger.LogDebug("Model {Id} already installed at {Path}", descriptor.Id, modelDir);
                return;
            }

            _logger.LogInformation(
                "Model {Id} is installed but missing {MissingCount} assets. Repairing installation.",
                descriptor.Id,
                missingAssets.Length);

            await _inflight.GetOrAdd(descriptor.Id, _ =>
                DownloadMissingAssetsAsync(descriptor, modelDir, manifestPath, missingAssets, progress, ct));
            return;
        }

        await _inflight.GetOrAdd(descriptor.Id, _ =>
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
            ValidateDiskSpace(modelDir, descriptor.SizeBytes);

            var assets = GetExpectedAssets(descriptor);

            var totalBytes = assets.Sum(a => a.SizeBytes > 0 ? a.SizeBytes : 0);
            if (totalBytes <= 0)
                totalBytes = descriptor.SizeBytes;
            _logger.LogInformation(
                "Starting model download {ModelId}: assetCount={AssetCount}, expectedBytes={TotalBytes}, targetDir={ModelDir}",
                descriptor.Id,
                assets.Count,
                totalBytes,
                modelDir);

            long downloadedBytes = 0;
            foreach (var asset in assets)
            {
                downloadedBytes += await DownloadAssetAsync(
                    descriptor.Id,
                    modelDir,
                    asset,
                    downloadedBytes,
                    totalBytes,
                    progress,
                    ct);
            }

            var manifest = ModelManifest.FromDescriptor(descriptor);
            await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), ct);

            _logger.LogDebug("Model {Id} downloaded to {Path}", descriptor.Id, modelDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model download failed for {ModelId}", descriptor.Id);
            throw;
        }
        finally
        {
            _inflight.TryRemove(descriptor.Id, out _);
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
                ValidateDiskSpace(modelDir, expectedBytes);

            long downloadedBytes = 0;
            var totalBytes = expectedBytes > 0 ? expectedBytes : descriptor.SizeBytes;
            _logger.LogInformation(
                "Repairing model assets for {ModelId}: missingCount={MissingCount}, expectedBytes={ExpectedBytes}",
                descriptor.Id,
                missingAssets.Count,
                totalBytes);
            foreach (var asset in missingAssets)
            {
                downloadedBytes += await DownloadAssetAsync(
                    descriptor.Id,
                    modelDir,
                    asset,
                    downloadedBytes,
                    totalBytes,
                    progress,
                    ct);
            }

            var manifest = ModelManifest.FromDescriptor(descriptor);
            await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), ct);
            _logger.LogDebug("Model {Id} assets repaired at {Path}", descriptor.Id, modelDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model asset repair failed for {ModelId}", descriptor.Id);
            throw;
        }
        finally
        {
            _inflight.TryRemove(descriptor.Id, out _);
        }
    }

    private async Task<long> DownloadAssetAsync(
        string modelId,
        string modelDir,
        ModelAsset asset,
        long downloadedBeforeAsset,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        var relativePath = NormalizeRelativePath(asset.RelativePath);
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

        var partPath = finalPath + ".part";
        var existingPartBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existingPartBytes > 0 && asset.SizeBytes > 0 && existingPartBytes > asset.SizeBytes)
        {
            _logger.LogWarning(
                "Discarding oversized part file: model={ModelId}, asset={AssetPath}, partBytes={PartBytes}, expectedBytes={ExpectedBytes}",
                modelId,
                relativePath,
                existingPartBytes,
                asset.SizeBytes);
            File.Delete(partPath);
            existingPartBytes = 0;
        }

        if (existingPartBytes > 0 && asset.SizeBytes > 0 && existingPartBytes == asset.SizeBytes)
        {
            File.Move(partPath, finalPath, overwrite: true);
            _logger.LogInformation(
                "Promoted completed part file without network request: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
                modelId,
                relativePath,
                existingPartBytes);
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + existingPartBytes, totalBytes));
            return existingPartBytes;
        }

        _logger.LogInformation(
            "Downloading model asset: model={ModelId}, asset={AssetPath}, resumeBytes={ResumeBytes}, url={Url}",
            modelId,
            relativePath,
            existingPartBytes,
            asset.DownloadUrl);

        var response = await SendAssetRequestAsync(asset.DownloadUrl, existingPartBytes, ct);
        if (existingPartBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
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
                return existingPartBytes;
            }

            _logger.LogWarning(
                "Received 416 for ranged request; restarting full download: model={ModelId}, asset={AssetPath}, resumeBytes={ResumeBytes}",
                modelId,
                relativePath,
                existingPartBytes);
            if (File.Exists(partPath))
                File.Delete(partPath);
            existingPartBytes = 0;
            response = await SendAssetRequestAsync(asset.DownloadUrl, existingPartBytes, ct);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
            _logger.LogInformation(
                "Model asset response: model={ModelId}, asset={AssetPath}, statusCode={StatusCode}",
                modelId,
                relativePath,
                (int)response.StatusCode);

            if (existingPartBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                File.Delete(partPath);
                existingPartBytes = 0;
            }

            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);

            long downloadedForAsset = existingPartBytes;
            await using (var fileStream = new FileStream(
                             partPath,
                             existingPartBytes > 0 ? FileMode.Append : FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedForAsset += bytesRead;
                    progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + downloadedForAsset, totalBytes));
                }

                await fileStream.FlushAsync(ct);
            }
            File.Move(partPath, finalPath, overwrite: true);
            _logger.LogInformation(
                "Completed model asset download: model={ModelId}, asset={AssetPath}, bytes={Bytes}",
                modelId,
                relativePath,
                downloadedForAsset);
            return downloadedForAsset;
        }
    }

    private async Task<HttpResponseMessage> SendAssetRequestAsync(string url, long resumeBytes, CancellationToken ct)
    {
        var effectiveUrl = ApplyMirror(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, effectiveUrl);
        if (resumeBytes > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeBytes, null);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private string ApplyMirror(string url)
    {
        if (string.IsNullOrWhiteSpace(_options.HuggingFaceMirror))
            return url;

        const string hfHost = "https://huggingface.co";
        if (url.StartsWith(hfHost, StringComparison.OrdinalIgnoreCase))
        {
            var mirror = _options.HuggingFaceMirror.TrimEnd('/');
            var rewritten = mirror + url[hfHost.Length..];
            _logger.LogDebug("Rewriting HuggingFace URL: {Original} → {Mirror}", url, rewritten);
            return rewritten;
        }

        return url;
    }

    private static string GetFileNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var name = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(name) ? "model.bin" : name;
    }

    private static IReadOnlyList<ModelAsset> GetExpectedAssets(ModelDescriptor descriptor) =>
        descriptor.Assets.Count > 0
            ? descriptor.Assets
            : [new ModelAsset(GetFileNameFromUrl(descriptor.DownloadUrl), descriptor.DownloadUrl, descriptor.SizeBytes)];

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', Path.DirectorySeparatorChar);

    public IReadOnlyList<InstalledModel> ListInstalled()
    {
        var storagePath = _options.ModelStoragePath;
        if (!Directory.Exists(storagePath))
            return [];

        var models = new List<InstalledModel>();
        foreach (var dir in Directory.GetDirectories(storagePath))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            var json = File.ReadAllText(manifestPath);
            var manifest = ModelManifest.FromJson(json);
            if (manifest is null)
            {
                _logger.LogWarning("Invalid manifest in {Dir}", dir);
                continue;
            }

            models.Add(new InstalledModel(
                manifest.Id, manifest.DisplayName, dir,
                manifest.SizeBytes, manifest.Type, manifest.DownloadedAt));
        }

        return models;
    }

    public async Task DeleteModelAsync(string modelId, CancellationToken ct)
    {
        var modelDir = GetModelDirectory(modelId);
        if (Directory.Exists(modelDir))
        {
            await Task.Run(() => Directory.Delete(modelDir, true), ct);
            _logger.LogDebug("Model {Id} deleted", modelId);
        }
    }

    public long GetTotalDiskUsage()
    {
        var storagePath = _options.ModelStoragePath;
        if (!Directory.Exists(storagePath))
            return 0;

        return Directory.EnumerateFiles(storagePath, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    public string GetModelDirectory(string modelId) =>
        Path.Combine(_options.ModelStoragePath, modelId);

    public async Task MigrateStoragePathAsync(string newPath, CancellationToken ct = default)
    {
        var oldPath = _options.ModelStoragePath;
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!Directory.Exists(oldPath))
        {
            _options.ModelStoragePath = newPath;
            _logger.LogDebug("Storage path changed to {Path} (no files to migrate)", newPath);
            return;
        }

        Directory.CreateDirectory(newPath);

        await Task.Run(() =>
        {
            foreach (var dir in Directory.GetDirectories(oldPath))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(newPath, dirName);

                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, true);

                try
                {
                    Directory.Move(dir, destDir);
                }
                catch (IOException)
                {
                    CopyDirectoryRecursive(dir, destDir);
                    Directory.Delete(dir, true);
                }

                _logger.LogDebug("Migrated model directory {Dir}", dirName);
            }
        }, ct);

        _options.ModelStoragePath = newPath;
        _logger.LogDebug("Storage path migrated from {Old} to {New}", oldPath, newPath);
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private void ValidateDiskSpace(string path, long requiredBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
        if (drive.AvailableFreeSpace < requiredBytes)
            throw new InsufficientDiskSpaceException(requiredBytes, drive.AvailableFreeSpace);
    }
}
