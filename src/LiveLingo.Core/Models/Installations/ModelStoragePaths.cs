namespace LiveLingo.Core.Models.Installations;

/// <summary>
/// Pure path arithmetic for the on-disk layout of installed models.
/// No I/O, no state — just rules for translating between IDs, descriptors,
/// asset relative paths, and the absolute file system locations the rest
/// of the model layer expects.
/// </summary>
internal static class ModelStoragePaths
{
    public const string ManifestFileName = "manifest.json";

    public static string GetModelDirectory(string storageRoot, string modelId) =>
        Path.Combine(storageRoot, modelId);

    public static string GetManifestPath(string modelDir) =>
        Path.Combine(modelDir, ManifestFileName);

    /// <summary>
    /// Normalises path separators so an asset whose relative path was
    /// authored on Windows still resolves correctly on Unix (and vice versa).
    /// </summary>
    public static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>
    /// Returns the asset list to materialise for the given descriptor,
    /// synthesising a single-file fallback when the descriptor declares no
    /// explicit assets.
    /// </summary>
    public static IReadOnlyList<ModelAsset> GetExpectedAssets(ModelDescriptor descriptor) =>
        descriptor.Assets.Count > 0
            ? descriptor.Assets
            : [new ModelAsset(GetFileNameFromUrl(descriptor.DownloadUrl), descriptor.DownloadUrl, descriptor.SizeBytes)];

    public static string GetFileNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var name = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(name) ? "model.bin" : name;
    }
}
