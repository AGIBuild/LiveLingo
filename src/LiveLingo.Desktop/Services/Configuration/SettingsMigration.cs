using System.Text.Json;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Services.Configuration;

/// <summary>
/// Brings a settings file written by version 1.x (schema 2) into the current schema. Only what still means the same
/// thing is kept: the hotkey, the interface language, update settings and the advanced paths and levels. Everything
/// else (translation engines, overlay, injection modes) belongs to a design that no longer exists.
/// </summary>
public static class SettingsMigration
{
    public const int LegacySchemaVersion = 2;

    public static SettingsModel FromLegacy(JsonElement root)
    {
        var model = SettingsModel.CreateDefault();

        if (Text(root, "hotkeys", "overlayToggle") is { } hotkey && IsValidHotkey(hotkey))
            model.Hotkeys.Summon = hotkey;
        if (Text(root, "ui", "language") is { } language)
            model.Appearance.Language = language;

        if (Text(root, "update", "updateUrl") is { } updateUrl)
            model.Update.UpdateUrl = updateUrl;
        if (Number(root, "update", "checkIntervalHours") is { } interval and > 0)
            model.Update.CheckIntervalHours = interval;

        model.Advanced.ModelStoragePath = Text(root, "advanced", "modelStoragePath");
        if (Number(root, "advanced", "inferenceThreads") is { } threads and >= 0)
            model.Advanced.InferenceThreads = threads;
        if (Text(root, "advanced", "logLevel") is { } logLevel)
            model.Advanced.LogLevel = logLevel;
        model.Advanced.HuggingFaceMirror = Text(root, "advanced", "huggingFaceMirror");
        return model;
    }

    private static bool IsValidHotkey(string gesture)
    {
        try
        {
            HotkeyParser.Parse("summon", gesture);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static JsonElement? Property(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }
        return null;
    }

    private static JsonElement? Nested(JsonElement root, string section, string name) =>
        Property(root, section) is { } sectionElement ? Property(sectionElement, name) : null;

    private static string? Text(JsonElement root, string section, string name) =>
        Nested(root, section, name) is { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;

    private static int? Number(JsonElement root, string section, string name) =>
        Nested(root, section, name) is { ValueKind: JsonValueKind.Number } element && element.TryGetInt32(out var value)
            ? value
            : null;
}
