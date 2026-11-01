using LiveLingo.Desktop.Settings;

namespace LiveLingo.Desktop.Web;

/// <param name="Theme">One of <see cref="SettingsViewModel.Themes"/>.</param>
internal sealed record GeneralSettingsState(
    string Hotkey, bool HotkeyInvalid, string CommitKey, string Language, string Theme, bool ReduceMotion);

internal sealed record TermState(int I, string Source, string Target);

/// <param name="Microphones">Device names; the first, empty one is the system default.</param>
internal sealed record VoiceSettingsState(
    IReadOnlyList<string> Microphones, string Microphone, string Direction, IReadOnlyList<TermState> Terms);

/// <summary>There is no key here, only whether one is stored: the key itself never goes back to the page.</summary>
/// <param name="Consent">The step the user is being asked to authorize, or null.</param>
internal sealed record PrivacySettingsState(
    bool CloudTranslate, bool CloudClean, string BaseUrl, string Model, bool HasKey, bool Invalid, CloudStep? Consent);

/// <param name="Text">What the old models are and how much room they take; empty when there are none.</param>
internal sealed record LegacyModelsState(int Count, string Text);

internal sealed record ModelsSettingsState(string Tier, LegacyModelsState Legacy, string Version);

/// <param name="Theme">One of <see cref="SettingsViewModel.Themes"/>.</param>
internal sealed record SettingsOptionsState(bool Animations, string Theme);

/// <summary>
/// Everything the settings page draws, as plain data (design 5.2). Like the composer's, it is replaced wholesale by
/// each snapshot. Built only by <see cref="From"/>.
/// </summary>
internal sealed record SettingsViewState(
    int Rev,
    string Section,
    GeneralSettingsState General,
    VoiceSettingsState Voice,
    PrivacySettingsState Privacy,
    ModelsSettingsState Models,
    SettingsOptionsState Options)
{
    public static SettingsViewState From(SettingsViewModel vm, SettingsTexts texts, string section, string version)
    {
        var theme = SettingsViewModel.Themes[vm.ThemeIndex];
        return new SettingsViewState(
            Rev: 0,
            section,
            new GeneralSettingsState(
                vm.SummonHotkey,
                vm.HotkeyInvalid,
                SettingsViewModel.CommitKeys[vm.CommitKeyIndex],
                SettingsViewModel.Languages[vm.LanguageIndex],
                theme,
                vm.ReduceMotion),
            new VoiceSettingsState(
                vm.MicrophoneNames,
                vm.MicrophoneNames[vm.MicrophoneIndex],
                SettingsViewModel.Targets[vm.TargetIndex],
                [.. vm.Terms.Select((term, i) => new TermState(i, term.Source, term.Target))]),
            new PrivacySettingsState(
                vm.CloudTranslate, vm.CloudClean, vm.CloudBaseUrl, vm.CloudModel, vm.HasStoredKey, vm.CloudInvalid, vm.PendingConsent),
            new ModelsSettingsState(
                SettingsViewModel.Tiers[vm.TierIndex],
                new LegacyModelsState(vm.LegacyCount, vm.HasLegacyModels ? texts.LegacyDescription(vm.LegacyCount, vm.LegacyBytes) : ""),
                version),
            new SettingsOptionsState(!vm.ReduceMotion, theme));
    }
}
