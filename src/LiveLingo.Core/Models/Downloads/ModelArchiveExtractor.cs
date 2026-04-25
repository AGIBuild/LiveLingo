using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace LiveLingo.Core.Models.Downloads;

/// <summary>
/// Unpacks an archive payload that was just downloaded for a model.
/// Currently supports <see cref="ModelArchiveType.TarBz2"/> (the format used by all
/// sherpa-onnx pretrained model bundles). Always flattens the archive's single
/// top-level directory so callers can address files relative to the model directory
/// without knowing the archive's internal layout.
/// </summary>
internal sealed class ModelArchiveExtractor
{
    private readonly ILogger _logger;

    public ModelArchiveExtractor(ILogger logger)
    {
        _logger = logger;
    }

    public void Extract(ModelDescriptor descriptor, string archivePath, string targetDirectory)
    {
        if (descriptor.ArchiveType == ModelArchiveType.None)
            return;

        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive file not found at {archivePath}", archivePath);

        Directory.CreateDirectory(targetDirectory);

        _logger.LogInformation(
            "Extracting model archive {ModelId}: format={ArchiveType}, source={Archive}, target={Target}",
            descriptor.Id, descriptor.ArchiveType, archivePath, targetDirectory);

        var topLevelPrefix = ResolveTopLevelPrefix(descriptor);

        using (var stream = File.OpenRead(archivePath))
        using (var reader = ReaderFactory.Open(stream))
        {
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                    continue;

                var entryPath = NormalizeEntryPath(reader.Entry.Key ?? string.Empty, topLevelPrefix);
                if (string.IsNullOrEmpty(entryPath))
                    continue;

                var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, entryPath));
                EnsureWithinTarget(targetDirectory, destinationPath);

                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                reader.WriteEntryToFile(destinationPath, new ExtractionOptions
                {
                    Overwrite = true,
                    PreserveFileTime = false,
                    ExtractFullPath = false
                });
            }
        }

        File.Delete(archivePath);
        _logger.LogDebug("Model archive {ModelId} extracted; archive payload removed", descriptor.Id);
    }

    /// <summary>
    /// Sherpa-onnx archives wrap their files inside a single top-level directory
    /// (e.g. <c>sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01/encoder.int8.onnx</c>).
    /// Strip that prefix so descriptor.ExtractedFiles can stay format-agnostic.
    /// </summary>
    private static string ResolveTopLevelPrefix(ModelDescriptor descriptor)
    {
        var fileName = Path.GetFileName(new Uri(descriptor.DownloadUrl).AbsolutePath);
        var dot = fileName.IndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static string NormalizeEntryPath(string entryKey, string topLevelPrefix)
    {
        var normalized = entryKey.Replace('\\', '/').TrimStart('/');
        if (!string.IsNullOrEmpty(topLevelPrefix) &&
            normalized.StartsWith(topLevelPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(topLevelPrefix.Length + 1)..];
        }
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static void EnsureWithinTarget(string targetDirectory, string destinationPath)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        if (!destinationPath.StartsWith(fullTarget, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Archive entry escapes target directory: {destinationPath}");
        }
    }
}
