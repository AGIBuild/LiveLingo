using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Services.Preferences;

/// <summary>What the user chose for one application the last time (an application is the scenario).</summary>
/// <param name="AppId">The platform's stable identity of the application; <see cref="ComposerPreferences.ClipboardId"/> for "no input box".</param>
/// <param name="UsedAt">When the user last chose something there; the oldest is dropped first beyond the capacity.</param>
public sealed record AppChoice(string AppId, string Name, ComposerOutput Output, TargetLanguage Target, DateTimeOffset UsedAt);

/// <summary>
/// Everything remembered, as stored. Only the identity of applications, two choices each and one export format:
/// never a window title, a text, a recording or a key.
/// </summary>
public sealed class PreferencesData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<AppChoice> Apps { get; set; } = [];
    public ExportFormat? ExportFormat { get; set; }
}

public interface IPreferencesStore
{
    /// <summary>What was stored; nothing when there is nothing (or nothing readable) yet.</summary>
    PreferencesData Load();

    void Save(PreferencesData data);
}
