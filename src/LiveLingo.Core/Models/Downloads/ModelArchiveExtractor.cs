using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Unpacks the archive payload of a model into its model directory. The archive's single top-level
/// directory (if any) is flattened so files are addressed relative to the model directory, and the
/// archive is removed afterwards. tar.gz and zip use the .NET readers (symbolic links and Unix file modes
/// are preserved); tar.bz2, which .NET cannot read, uses SharpCompress.
/// </summary>
internal sealed class ModelArchiveExtractor(ILogger logger)
{
    private const string StagingPrefix = ".extract-";

    public void Extract(ModelDescriptor descriptor, string archivePath, string targetDirectory)
    {
        if (descriptor.ArchiveType == ModelArchiveType.None)
            throw new InvalidOperationException($"Model '{descriptor.Id}' does not declare an archive format.");
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive file not found at {archivePath}", archivePath);

        Directory.CreateDirectory(targetDirectory);
        logger.LogInformation(
            "Extracting model archive {ModelId}: format={ArchiveType}, source={Archive}, target={Target}",
            descriptor.Id, descriptor.ArchiveType, archivePath, targetDirectory);

        var staging = Path.Combine(targetDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractTo(descriptor.ArchiveType, archivePath, staging);
            MoveContents(staging, targetDirectory);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }

        File.Delete(archivePath);
        logger.LogDebug("Model archive {ModelId} extracted; archive payload removed", descriptor.Id);
    }

    private static void ExtractTo(ModelArchiveType type, string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        switch (type)
        {
            case ModelArchiveType.TarGz:
                using (var file = File.OpenRead(archivePath))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
                break;
            case ModelArchiveType.Zip:
                ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
                break;
            case ModelArchiveType.TarBz2:
                using (var file = File.OpenRead(archivePath))
                using (var reader = ReaderFactory.Open(file))
                    reader.WriteAllToDirectory(destination, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    private static void MoveContents(string staging, string targetDirectory)
    {
        var directories = Directory.GetDirectories(staging);
        var source = directories.Length == 1 && Directory.GetFiles(staging).Length == 0 ? directories[0] : staging;
        foreach (var entry in Directory.GetFileSystemEntries(source))
        {
            var destination = Path.Combine(targetDirectory, Path.GetFileName(entry));
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            else if (File.Exists(destination))
                File.Delete(destination);

            if (Directory.Exists(entry) && !IsSymbolicLink(entry))
                Directory.Move(entry, destination);
            else
                File.Move(entry, destination);
        }
    }

    private static bool IsSymbolicLink(string path) => new FileInfo(path).LinkTarget is not null;
}
