using System.Globalization;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Settings;

/// <summary>Turns the settings state into localized text, so the page only draws strings.</summary>
public sealed class SettingsTexts(ILocalizationService loc)
{
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
        "direction", "direction.desc", "direction.zhEn", "direction.enZh",
        "terms", "terms.source", "terms.target", "terms.sourceExample", "terms.targetExample", "terms.add", "terms.remove", "terms.hint",
        "cloud.translate", "cloud.translate.desc", "cloud.clean", "cloud.clean.desc",
        "cloud.baseUrl", "cloud.baseUrl.desc", "cloud.model", "cloud.model.desc", "cloud.apiKey", "cloud.apiKey.desc",
        "cloud.keyStored", "cloud.forget", "cloud.invalid",
        "consent.translate", "consent.clean", "consent.body", "consent.keepLocal", "consent.allow",
        "recent", "recent.desc", "recent.clear",
        "models.speech", "models.clean", "models.translate",
        "model", "model.desc", "model.quality", "model.fast",
        "legacy", "legacy.remove",
        "version", "version.desc"
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

    /// <summary>What the old models still on disk are and how much room they take.</summary>
    public string LegacyDescription(int count, long bytes) =>
        loc.T("settings.legacy.desc", count, (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture));
}
