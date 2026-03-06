namespace LiveLingo.Core.Models;

public record ModelDescriptor(
    string Id,
    string DisplayName,
    string DownloadUrl,
    long SizeBytes,
    ModelType Type);

public enum ModelType
{
    Translation,
    PostProcessing,
    LanguageDetection
}
