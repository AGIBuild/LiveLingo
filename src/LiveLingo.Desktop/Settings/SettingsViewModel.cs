using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Settings;

/// <summary>Edits a copy of the settings; nothing changes until <see cref="SaveCommand"/> succeeds.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Languages = ["zh-CN", "en-US"];

    private readonly ISettingsService _settings;
    private readonly SettingsModel _draft;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        _draft = settings.CloneCurrent();
        _summonHotkey = _draft.Hotkeys.Summon;
        _language = _draft.Appearance.Language;
        _quality = _draft.Composer.TranslatorTier == nameof(TranslatorTier.Quality);
        _glossary = _draft.Composer.Glossary;
    }

    /// <summary>Raised after a successful save or a cancel; the window should close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string _summonHotkey;
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _quality;
    [ObservableProperty] private string _glossary;

    /// <summary>True when the hotkey text is not a valid shortcut; the window shows the localized message.</summary>
    [ObservableProperty] private bool _hotkeyInvalid;

    partial void OnSummonHotkeyChanged(string value) => HotkeyInvalid = false;

    [RelayCommand]
    private void Save()
    {
        try
        {
            HotkeyParser.Parse("summon", SummonHotkey);
        }
        catch (ArgumentException)
        {
            HotkeyInvalid = true;
            return;
        }

        _draft.Hotkeys.Summon = SummonHotkey.Trim();
        _draft.Appearance.Language = Language;
        _draft.Composer.TranslatorTier = Quality ? nameof(TranslatorTier.Quality) : nameof(TranslatorTier.Fast);
        _draft.Composer.Glossary = Glossary;
        _settings.Replace(_draft);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
