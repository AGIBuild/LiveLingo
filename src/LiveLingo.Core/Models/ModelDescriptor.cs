namespace LiveLingo.Core.Models;

public record ModelDescriptor(
    string Id,
    string DisplayName,
    string DownloadUrl,
    long SizeBytes,
    ModelType Type)
{
    public IReadOnlyList<ModelAsset> Assets { get; init; } = [];
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
