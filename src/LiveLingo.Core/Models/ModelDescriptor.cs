namespace LiveLingo.Core.Models;

public record ModelDescriptor(
    string Id,
    string DisplayName,
    string DownloadUrl,
    long SizeBytes,
    ModelType Type)
{
    public IReadOnlyList<ModelAsset> Assets { get; init; } = [];

    /// <summary>
    /// When set, runtime may switch to this descriptor if the primary model fails to load (e.g. insufficient RAM).
    /// </summary>
    public ModelDescriptor? LoadFailureFallback { get; init; }
}

public record ModelAsset(
    string RelativePath,
    string DownloadUrl,
    long SizeBytes);

public enum ModelType
{
    Translation,
    PostProcessing,
    LanguageDetection,
    SpeechToText,
    VoiceActivityDetection
}
