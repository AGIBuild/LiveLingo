using System.Text.Json;

namespace LiveLingo.Core.Models;

public record ModelManifest(
    string Id,
    string DisplayName,
    long SizeBytes,
    ModelType Type,
    DateTime DownloadedAt)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ModelManifest FromDescriptor(ModelDescriptor d) =>
        new(d.Id, d.DisplayName, d.SizeBytes, d.Type, DateTime.UtcNow);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ModelManifest? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ModelManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
