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
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + existingFileSize, totalBytes));
            return existingFileSize;
        }

        var partPath = finalPath + ".part";
        var existingPartBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        if (existingPartBytes > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingPartBytes, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (existingPartBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            File.Delete(partPath);
            existingPartBytes = 0;
        }

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            partPath,
            existingPartBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        long downloadedForAsset = existingPartBytes;
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedForAsset += bytesRead;
            progress?.Report(new ModelDownloadProgress(modelId, downloadedBeforeAsset + downloadedForAsset, totalBytes));
        }

        await fileStream.FlushAsync(ct);
        File.Move(partPath, finalPath, overwrite: true);
        return downloadedForAsset;
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
