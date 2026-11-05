using System.Globalization;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Update;

namespace LiveLingo.Desktop.Settings;

/// <summary>Turns the settings state into localized text, so the page only draws strings.</summary>
public sealed class SettingsTexts(ILocalizationService loc)
{
    private readonly ComposerTexts _composer = new(loc);

    /// <summary>The static labels of the page, by their key without the "settings." prefix.</summary>
    private static readonly string[] Keys =
    [
        "title", "nav.aria", "nav.general", "nav.voice", "nav.privacy", "nav.models",
        "hotkey", "hotkey.desc", "hotkey.invalid", "hotkey.prompt",
        "commit", "commit.desc", "commit.enter", "commit.ctrlEnter",
        "language", "language.desc",
        "theme", "theme.desc", "theme.auto", "theme.light", "theme.dark",
        "motion", "motion.desc",
        "mic", "mic.desc", "mic.default",
        "cleanup", "cleanup.desc",
        "direction", "direction.desc", "direction.zhEn", "direction.enZh",
        "output", "output.desc", "output.clean", "output.translate", "output.original",
        "autoDictate", "autoDictate.desc", "preload", "preload.desc", "remember", "remember.desc",
        "polish", "polish.desc", "polish.reset", "polish.custom", "polish.invalid", "polish.save",
        "apps", "apps.desc", "apps.empty", "apps.forget", "apps.clear",
        "terms", "terms.source", "terms.target", "terms.sourceExample", "terms.targetExample", "terms.add", "terms.remove", "terms.hint",
        "cloud.translate", "cloud.translate.desc", "cloud.clean", "cloud.clean.desc",
        "cloud.baseUrl", "cloud.baseUrl.desc", "cloud.model", "cloud.model.desc", "cloud.apiKey", "cloud.apiKey.desc",
        "cloud.keyStored", "cloud.invalid",
        "cloud.endpoints", "cloud.endpoints.active", "cloud.endpoints.none", "cloud.endpoints.empty",
        "cloud.add", "cloud.edit", "cloud.delete", "cloud.enabled", "cloud.enable", "cloud.keySaved", "cloud.keyMissing",
        "cloud.name", "cloud.name.example", "cloud.form.add", "cloud.form.edit", "cloud.form.save", "cloud.form.create", "cloud.form.cancel",
        "cloud.error.nameMissing", "cloud.error.nameTaken", "cloud.error.urlInvalid", "cloud.error.modelMissing", "cloud.error.keyMissing",
        "cloud.switch.title", "cloud.switch.body", "cloud.switch.keep", "cloud.switch.confirm",
        "cloud.delete.title", "cloud.delete.body", "cloud.delete.steps", "cloud.delete.keep", "cloud.delete.confirm",
        "consent.translate", "consent.clean", "consent.body", "consent.keepLocal", "consent.allow",
        "recent", "recent.desc", "recent.clear",
        "models.speech", "models.clean", "models.translate",
        "model", "model.desc", "model.quality", "model.fast",
        "legacy", "legacy.remove",
        "version", "version.desc",
        "update", "update.desc", "update.check", "update.restart", "update.download",
        "diag", "diag.desc", "diag.open", "diag.title"
    ];

    /// <summary>The interface language, for the page's lang attribute.</summary>
    public string Locale => loc.CurrentCulture.Name;

    public string Title => loc.T("settings.title");

    public IReadOnlyDictionary<string, string> WebStrings()
    {
        var strings = Keys.ToDictionary(key => key, key => loc.T("settings." + key));
        strings["closeAria"] = loc.T("composer.close");
        return strings;
    }

    /// <summary>A label by its key without the "settings." prefix.</summary>
    public string Label(string key) => loc.T("settings." + key);

    public string OnOff(bool on) => loc.T(on ? "settings.diag.on" : "settings.diag.off");

    public string Stored(bool stored) => loc.T(stored ? "settings.diag.stored" : "settings.diag.notStored");

    /// <summary>What the old models still on disk are and how much room they take.</summary>
    public string LegacyDescription(int count, long bytes) =>
        loc.T("settings.legacy.desc", count, (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture));

    /// <summary>The application's name; the context without an input box (copy only) has none.</summary>
    public string AppName(string name) => _composer.ContextName(name);

    /// <summary>The remembered choice in the words of the output menu.</summary>
    public string AppSummary(ComposerOutput output, TargetLanguage target) => _composer.OutputMenu(output, target);

    public string Date(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>What the update check is doing or found, as one sentence; the user's installed <paramref name="current"/> version names the "latest".</summary>
    public string UpdateStatus(UpdateState state, string current) => state.Phase switch
    {
        UpdatePhase.Off => loc.T("settings.update.off"),
        UpdatePhase.Unsupported => loc.T("settings.update.unsupported"),
        UpdatePhase.Idle => loc.T("settings.update.idle"),
        UpdatePhase.Checking => loc.T("settings.update.checking"),
        UpdatePhase.Downloading => loc.T("settings.update.downloading", state.Version!, state.Percent),
        UpdatePhase.Ready => loc.T("settings.update.ready", state.Version!),
        UpdatePhase.UpToDate => loc.T("settings.update.latest", current, Moment(state.At!.Value)),
        UpdatePhase.Failed => loc.T("settings.update.failed", Moment(state.At!.Value), state.Error!),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state.Phase, "Not an update phase.")
    };

    private static string Moment(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
