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
            _logger.LogDebug("Model {Id} already installed at {Path}", descriptor.Id, modelDir);
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

            var partPath = manifestPath + ".part";
            long existingBytes = 0;
            if (File.Exists(partPath))
                existingBytes = new FileInfo(partPath).Length;

            using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.DownloadUrl);
            if (existingBytes > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = descriptor.SizeBytes;
            var downloaded = existingBytes;

            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(partPath,
                existingBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                progress?.Report(new ModelDownloadProgress(descriptor.Id, downloaded, totalBytes));
            }

            fileStream.Close();

            var manifest = ModelManifest.FromDescriptor(descriptor);
            await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), ct);

            if (File.Exists(partPath))
                File.Delete(partPath);

            _logger.LogInformation("Model {Id} downloaded successfully", descriptor.Id);
        }
        finally
        {
            _inflight.TryRemove(descriptor.Id, out _);
        }
    }

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
            _logger.LogInformation("Model {Id} deleted", modelId);
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

    private void ValidateDiskSpace(string path, long requiredBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
        if (drive.AvailableFreeSpace < requiredBytes)
            throw new InsufficientDiskSpaceException(requiredBytes, drive.AvailableFreeSpace);
    }
}
