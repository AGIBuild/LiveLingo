using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models.Installations;

/// <summary>
/// Moves the on-disk model directory tree from the old <see cref="CoreOptions.ModelStoragePath"/>
/// to a user-chosen new location, then mutates the option so subsequent reads see the new root.
/// Falls back to copy-then-delete when a cross-volume rename fails.
/// </summary>
internal sealed class ModelStoragePathMigrator
{
    private readonly CoreOptions _options;
    private readonly ILogger _logger;

    public ModelStoragePathMigrator(CoreOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task MigrateAsync(string newPath, CancellationToken ct)
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

        await Task.Run(() => MoveDirectoryContents(oldPath, newPath, ct), ct).ConfigureAwait(false);

        _options.ModelStoragePath = newPath;
        _logger.LogDebug("Storage path migrated from {Old} to {New}", oldPath, newPath);
    }

    private void MoveDirectoryContents(string sourceRoot, string destinationRoot, CancellationToken ct)
    {
        foreach (var dir in Directory.GetDirectories(sourceRoot))
        {
            ct.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(dir);
            var destDir = Path.Combine(destinationRoot, dirName);

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
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
