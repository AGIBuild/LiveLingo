namespace LiveLingo.Core.Models;

/// <summary>What an installable artifact is used for.</summary>
public enum ModelType
{
    Runtime,
    Translation,
    PostProcessing,
    SpeechToText,
    VoiceActivityDetection
}

/// <summary>Archive format of a downloaded payload that must be unpacked into the model directory.</summary>
public enum ModelArchiveType
{
    /// <summary>The downloaded files are used as they are (for example a .gguf or .onnx file).</summary>
    None,

    /// <summary>tar archive compressed with bzip2 (sherpa-onnx model bundles).</summary>
    TarBz2,

    /// <summary>tar archive compressed with gzip (llama.cpp macOS release).</summary>
    TarGz,

    /// <summary>zip archive (llama.cpp Windows release).</summary>
    Zip
}

/// <summary>One downloadable file. The URL must pin an immutable revision and <paramref name="Sha256"/> is verified after download.</summary>
public record ModelAsset(
    string RelativePath,
    string DownloadUrl,
    long SizeBytes,
    string Sha256);

public record ModelDescriptor(
    string Id,
    string DisplayName,
    ModelType Type,
    string License,
    IReadOnlyList<ModelAsset> Assets)
{
    /// <summary>The name without its description ("Hy-MT2-1.8B"), for one line of status text; <see cref="DisplayName"/> is the full one.</summary>
    public string ShortName { get; init; } = DisplayName;

    /// <summary>Total download size in bytes.</summary>
    public long SizeBytes => Assets.Sum(asset => asset.SizeBytes);

    /// <summary>
    /// Files that must be present after extraction for an archive-based model to count as installed.
    /// Paths are relative to the model directory.
    /// </summary>
    public IReadOnlyList<string> ExtractedFiles { get; init; } = [];

    /// <summary>
    /// When set, the single asset is an archive that is unpacked into the model directory
    /// (its single top-level directory is flattened) and then removed.
    /// </summary>
    public ModelArchiveType ArchiveType { get; init; } = ModelArchiveType.None;
}
