using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.Preferences;

/// <summary>
/// The one place that decides where a composer begins and what is remembered between uses (design: user
/// preferences). The unit of memory is the target application: the output (clean up or translate) and the translation
/// direction last chosen there are used the next time the composer opens for it. Everything else is either a setting,
/// derived from the situation, or only for the current draft.
/// </summary>
public sealed class ComposerPreferences
{
    /// <summary>Applications remembered at most; the one chosen in longest ago is dropped first.</summary>
    public const int Capacity = 50;

    /// <summary>The context of a composer without an input box, where the result only goes to the clipboard.</summary>
    public const string ClipboardId = "·clipboard";

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
    /// possible, otherwise the settings' defaults. A remembered output that is no longer offered (cleanup was turned
    /// off since) is simply not applicable and ignored.
    /// </summary>
    public ComposerStart Resolve(TargetWindowInfo? target, IReadOnlyList<ComposerOutput> offered)
    {
        var composer = _settings.Current.Composer;
        var defaultOutput = Enum.Parse<ComposerOutput>(composer.DefaultOutput);
        if (!offered.Contains(defaultOutput))
            defaultOutput = ComposerOutput.Original;
        var defaultTarget = Enum.Parse<TargetLanguage>(composer.TargetLanguage);

        var key = Enabled ? KeyOf(target) : null;
        if (key is null)
            return new ComposerStart(defaultOutput, defaultTarget, defaultOutput, defaultTarget, null);

        var name = target?.ProcessName ?? "";
        if (_data.Apps.FirstOrDefault(app => app.AppId == key) is { } choice && offered.Contains(choice.Output))
        {
            var differs = choice.Output != defaultOutput || choice.Target != defaultTarget;
            return new ComposerStart(
                choice.Output, choice.Target, defaultOutput, defaultTarget, new RememberContext(key, name, differs));
        }

        return new ComposerStart(defaultOutput, defaultTarget, defaultOutput, defaultTarget, new RememberContext(key, name, false));
    }

    /// <summary>
    /// The user chose an output or a direction for <paramref name="target"/>. The original wording is never
    /// remembered (it skips cleanup and the fidelity check, so it stays a choice made each time); the direction is.
    /// </summary>
    public void Remember(TargetWindowInfo? target, ComposerOutput output, TargetLanguage language)
    {
        if (!Enabled || KeyOf(target) is not { } key)
            return;

        var existing = _data.Apps.FirstOrDefault(app => app.AppId == key);
        var remembered = output != ComposerOutput.Original
            ? output
            : existing?.Output ?? Enum.Parse<ComposerOutput>(_settings.Current.Composer.DefaultOutput);

        _data.Apps.RemoveAll(app => app.AppId == key);
        _data.Apps.Insert(0, new AppChoice(key, target?.ProcessName ?? "", remembered, language, _time.GetUtcNow()));
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
