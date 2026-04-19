using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Installations;

/// <summary>
/// Read/inspect/delete operations against the on-disk install layout.
/// Reads <c>manifest.json</c> per model directory to enumerate installed models;
/// computes total disk usage; deletes a model's directory tree.
/// </summary>
internal sealed class InstalledModelStore
{
    private readonly CoreOptions _options;
    private readonly ILogger _logger;

    public InstalledModelStore(CoreOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public string GetModelDirectory(string modelId) =>
        ModelStoragePaths.GetModelDirectory(_options.ModelStoragePath, modelId);

    public IReadOnlyList<InstalledModel> List()
    {
        var storagePath = _options.ModelStoragePath;
        if (!Directory.Exists(storagePath))
            return [];

        var models = new List<InstalledModel>();
        foreach (var dir in Directory.GetDirectories(storagePath))
        {
            var manifestPath = ModelStoragePaths.GetManifestPath(dir);
            if (!File.Exists(manifestPath))
                continue;

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

    public bool HasAllExpectedLocalAssets(ModelDescriptor descriptor)
    {
        var modelDir = GetModelDirectory(descriptor.Id);
        if (!Directory.Exists(modelDir))
            return false;

        foreach (var rel in ModelStoragePaths.GetExpectedInstalledFiles(descriptor))
        {
            var path = Path.Combine(modelDir, rel);
            if (!File.Exists(path))
                return false;
        }

        return true;
    }

    public async Task DeleteAsync(string modelId, CancellationToken ct)
    {
        var modelDir = GetModelDirectory(modelId);
        if (Directory.Exists(modelDir))
        {
            await Task.Run(() => Directory.Delete(modelDir, true), ct).ConfigureAwait(false);
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
}
