using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveLingo.Desktop.Web;

/// <summary>The one JSON dialect of the C#/page contract: camelCase, strict enums, nothing implicit.</summary>
internal static class WebJson
{
    private static JsonSerializerOptions Create(bool fixture) => new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = fixture,
        // Fixtures are read by people and never evaluated; the wire keeps the default escaping of markup characters.
        Encoder = fixture ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    /// <summary>Messages on the wire, in both directions.</summary>
    public static JsonSerializerOptions Wire { get; } = Create(fixture: false);

    /// <summary>Golden fixtures: the same shape, readable in a diff.</summary>
    public static JsonSerializerOptions Fixture { get; } = Create(fixture: true);
}
