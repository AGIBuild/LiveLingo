using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.Preferences;

/// <summary>
/// The one place that decides where a composer begins and what is remembered between uses (design: user
/// preferences). The unit of memory is the target application: the output, scenario polish mode and translation
/// direction last chosen there are used the next time the composer opens for it.
/// </summary>
public sealed class ComposerPreferences
{
    /// <summary>Applications remembered at most; the one chosen in longest ago is dropped first.</summary>
    public const int Capacity = 50;

    /// <summary>The context of a composer without an input box, where the result only goes to the clipboard.</summary>
    public const string ClipboardId = "·clipboard";

    private static readonly string[] AiPairingHosts =
    [
        "cursor", "vscode", "code", "windsurf", "claude", "chatgpt", "warp", "iterm", "terminal",
        "windowsterminal", "wezterm", "ghostty", "alacritty", "zed", "jetbrains", "rider", "idea", "pycharm", "webstorm", "goland"
    ];
    private static readonly string[] TrackerHosts =
        ["linear", "jira", "reminders", "todoist", "things", "ticktick", "omnifocus"];
    private static readonly string[] DocumentHosts =
        ["notion", "obsidian", "feishu doc", "lark doc", "confluence", "craft", "onenote", "winword", "microsoft.word", "pages"];
    private static readonly string[] MailHosts =
        ["apple.mail", "outlook", "thunderbird", "mimestream", "superhuman"];

    private readonly IPreferencesStore _store;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly PreferencesData _data;

    public ComposerPreferences(IPreferencesStore store, ISettingsService settings, TimeProvider? time = null)
    {
        _store = store;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        _data = store.Load();
    }

    /// <summary>The remembered applications, the one chosen in most recently first.</summary>
    public IReadOnlyList<AppChoice> Apps => _data.Apps;

    public ExportFormat? LastExportFormat => _data.ExportFormat;

    private bool Enabled => _settings.Current.Composer.RememberChoices;

    /// <summary>
    /// Where the composer for <paramref name="target"/> begins: the remembered choice when there is one that is still
    /// possible, otherwise the host-category smart preset (when enabled), otherwise the settings' defaults.
    /// </summary>
    public ComposerStart Resolve(TargetWindowInfo? target, IReadOnlyList<ComposerOutput> offered)
    {
        var composer = _settings.Current.Composer;
        var defaultOutput = Enum.Parse<ComposerOutput>(composer.DefaultOutput);
        if (!offered.Contains(defaultOutput))
            defaultOutput = ComposerOutput.Original;
        var defaultTarget = Enum.Parse<TargetLanguage>(composer.TargetLanguage);
        var enabledPresets = composer.EffectivePolishPresets().Where(p => p.Enabled).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var defaultPolish = composer.ResolvedDefaultPolishMode();

        var smartPolish = composer.SmartHostPolish && defaultOutput == ComposerOutput.Clean
            ? SmartPolishModeFor(target)
            : null;
        var basePolish = smartPolish is not null && enabledPresets.Contains(smartPolish) ? smartPolish : defaultPolish;

        var key = Enabled ? KeyOf(target) : null;
        if (key is null)
        {
            return new ComposerStart(
                defaultOutput, defaultTarget, defaultOutput, defaultTarget, null,
                PolishMode: basePolish, DefaultPolishMode: defaultPolish);
        }

        var name = target?.ProcessName ?? "";
        if (_data.Apps.FirstOrDefault(app => app.AppId == key) is { } choice && offered.Contains(choice.Output))
        {
            var resolvedMode = !string.IsNullOrWhiteSpace(choice.PolishMode) && enabledPresets.Contains(choice.PolishMode)
                ? choice.PolishMode
                : defaultPolish;
            var differs = choice.Output != defaultOutput
                          || choice.Target != defaultTarget
                          || (choice.Output == ComposerOutput.Clean && !string.Equals(resolvedMode, defaultPolish, StringComparison.Ordinal));
            return new ComposerStart(
                choice.Output, choice.Target, defaultOutput, defaultTarget, new RememberContext(key, name, differs),
                PolishMode: resolvedMode, DefaultPolishMode: defaultPolish);
        }

        return new ComposerStart(
            defaultOutput, defaultTarget, defaultOutput, defaultTarget, new RememberContext(key, name, false),
            PolishMode: basePolish, DefaultPolishMode: defaultPolish);
    }

    /// <summary>Maps known target host applications to their highest-leverage polish mode out of the box.</summary>
    public static string? SmartPolishModeFor(TargetWindowInfo? target)
    {
        if (target is null)
            return null;

        var probe = $"{target.AppId} {target.ProcessName}".ToLowerInvariant();
        if (MatchesAnyHost(probe, AiPairingHosts))
            return PolishPresets.AiTaskId;
        if (MatchesAnyHost(probe, TrackerHosts))
            return PolishPresets.TodoId;
        if (MatchesAnyHost(probe, DocumentHosts))
            return PolishPresets.ReportId;
        if (MatchesAnyHost(probe, MailHosts))
            return PolishPresets.FormalId;
        return null;
    }

    private static bool MatchesAnyHost(string probe, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            var index = probe.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                var startOk = index == 0 || !char.IsAsciiLetterOrDigit(probe[index - 1]);
                var endIdx = index + token.Length;
                var endOk = endIdx == probe.Length || !char.IsAsciiLetterOrDigit(probe[endIdx]);
                if (startOk && endOk)
                    return true;
                index = probe.IndexOf(token, index + 1, StringComparison.Ordinal);
            }
        }
        return false;
    }

    /// <summary>
    /// The user chose an output, polish mode or direction for <paramref name="target"/>. The original wording is never
    /// remembered (it skips cleanup and the fidelity check, so it stays a choice made each time).
    /// </summary>
    public void Remember(
        TargetWindowInfo? target,
        ComposerOutput output,
        TargetLanguage language,
        string? polishMode = null)
    {
        if (!Enabled || KeyOf(target) is not { } key)
            return;

        var existing = _data.Apps.FirstOrDefault(app => app.AppId == key);
        var remembered = output != ComposerOutput.Original
            ? output
            : existing?.Output ?? Enum.Parse<ComposerOutput>(_settings.Current.Composer.DefaultOutput);
        var mode = !string.IsNullOrWhiteSpace(polishMode)
            ? polishMode!
            : !string.IsNullOrWhiteSpace(existing?.PolishMode)
                ? existing!.PolishMode
                : _settings.Current.Composer.ResolvedDefaultPolishMode();

        SaveAppEntry(key, target?.ProcessName ?? "", remembered, language, mode);
    }

    /// <summary>Sets or updates an application's remembered choice directly from the Settings page.</summary>
    public void SetAppChoice(string appId, string name, ComposerOutput output, TargetLanguage language, string polishMode)
    {
        if (string.IsNullOrWhiteSpace(appId) || output == ComposerOutput.Original)
            return;

        var resolvedMode = string.IsNullOrWhiteSpace(polishMode)
            ? _settings.Current.Composer.ResolvedDefaultPolishMode()
            : polishMode;
        SaveAppEntry(appId, name, output, language, resolvedMode);
    }

    public void ForgetPolishMode(string modeId)
    {
        var changed = false;
        for (var i = 0; i < _data.Apps.Count; i++)
        {
            if (string.Equals(_data.Apps[i].PolishMode, modeId, StringComparison.Ordinal))
            {
                _data.Apps[i] = _data.Apps[i] with { PolishMode = "" };
                changed = true;
            }
        }
        if (changed)
            _store.Save(_data);
    }

    private void SaveAppEntry(string key, string name, ComposerOutput output, TargetLanguage language, string polishMode)
    {
        var preservedName = string.IsNullOrEmpty(name)
            ? _data.Apps.FirstOrDefault(app => app.AppId == key)?.Name ?? ""
            : name;
        _data.Apps.RemoveAll(app => app.AppId == key);
        _data.Apps.Insert(0, new AppChoice(key, preservedName, output, language, _time.GetUtcNow(), polishMode));
        if (_data.Apps.Count > Capacity)
            _data.Apps.RemoveRange(Capacity, _data.Apps.Count - Capacity);
        _store.Save(_data);
    }

    public void Forget(string appId)
    {
        if (_data.Apps.RemoveAll(app => app.AppId == appId) > 0)
            _store.Save(_data);
    }

    /// <summary>Forgets everything that was remembered.</summary>
    public void Clear()
    {
        _data.Apps.Clear();
        _data.ExportFormat = null;
        _store.Save(_data);
    }

    public void RememberExportFormat(ExportFormat format)
    {
        _data.ExportFormat = format;
        _store.Save(_data);
    }

    /// <summary>Identity of the context, or null for a target the platform could not identify (it gets no memory).</summary>
    private static string? KeyOf(TargetWindowInfo? target) =>
        target is null ? ClipboardId : target.AppId.Length == 0 ? null : target.AppId;
}
