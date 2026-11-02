using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Services.Preferences;

/// <summary>
/// <c>preferences.json</c> next to the settings. It is not part of the settings: those are written by the user and can
/// be exported; this is recorded by use and can be thrown away at any time.
/// </summary>
public sealed class JsonPreferencesStore(string filePath, ILogger<JsonPreferencesStore>? logger = null) : IPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PreferencesData Load()
    {
        if (!File.Exists(filePath))
            return new PreferencesData();

        try
        {
            var data = JsonSerializer.Deserialize<PreferencesData>(File.ReadAllText(filePath), JsonOptions);
            if (data is { SchemaVersion: PreferencesData.CurrentSchemaVersion })
                return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Not a loss worth stopping for: what was remembered is only a convenience. The log never has its content.
            logger?.LogWarning("The remembered choices could not be read ({Kind}); starting without them", ex.GetType().Name);
            return new PreferencesData();
        }

        logger?.LogWarning("The remembered choices are of another version; starting without them");
        return new PreferencesData();
    }

    public void Save(PreferencesData data)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temporary, filePath, overwrite: true);
    }
}
