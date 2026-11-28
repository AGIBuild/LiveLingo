using LiveLingo.Core.Inference;

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

/// <summary>
/// Quality rank, relative ratings (1..3) and empirical resident memory footprint of a user-selectable model
/// within its <see cref="ModelStage"/>. <paramref name="Rank"/> 1 is the highest-quality option in that stage.
/// </summary>
/// <param name="Stage">The user-facing pipeline stage this model serves.</param>
/// <param name="Rank">1-based quality rank within <paramref name="Stage"/> (1 = highest quality, ascending = lighter).</param>
/// <param name="QualityStars">Relative quality score from 1 to 3.</param>
/// <param name="SpeedStars">Relative speed score from 1 to 3.</param>
/// <param name="ResidentBytes">
/// Empirical steady-state resident memory in bytes after warm inference at <c>ctx-size = 4096</c>
/// (for <see cref="ModelStage.Speech"/>, includes the companion live streaming recognizer and ONNX runtime).
/// </param>
/// <param name="RequiresGpu">
/// True when CPU-only inference exceeds interactive latency bounds due to memory-bandwidth limits (&gt; 4 GB quantized weights).
/// </param>
/// <param name="UtilityScore">
/// Evidence-weighted quality contribution used by <see cref="ModelAdvisor"/> when optimizing multi-stage residency budgets.
/// </param>
public sealed record ModelProfile(
    ModelStage Stage,
    int Rank,
    int QualityStars,
    int SpeedStars,
    long ResidentBytes,
    bool RequiresGpu,
    int UtilityScore);

public record ModelDescriptor(
    string Id,
    string DisplayName,
    ModelType Type,
    string License,
    IReadOnlyList<ModelAsset> Assets)
{
    /// <summary>The name without its description ("Hy-MT2-1.8B"), for one line of status text; <see cref="DisplayName"/> is the full one.</summary>
    public string ShortName { get; init; } = DisplayName;

    /// <summary>
    /// Underlying shared catalog artifact when multiple stage options (for example unified Cleaner and Translator roles)
    /// execute the exact same GGUF weights on disk and share a single <c>llama-server</c> instance.
    /// </summary>
    public ModelDescriptor? SharedArtifact { get; init; }

    /// <summary>Returns the physical installable artifact on disk (<see cref="SharedArtifact"/> when shared, otherwise <c>this</c>).</summary>
    public ModelDescriptor ResolveArtifact() => SharedArtifact ?? this;

    /// <summary>On-disk directory and shared server identity.</summary>
    public string ArtifactId => (SharedArtifact ?? this).Id;

    /// <summary>Total download size in bytes.</summary>
    public long SizeBytes => (SharedArtifact ?? this).Assets.Sum(asset => asset.SizeBytes);

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

    /// <summary>Capability and ranking metadata when this descriptor is a user-selectable stage option; null for fixed runtimes and detectors.</summary>
    public ModelProfile? Profile { get; init; }
}
