using System.Globalization;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Services.Update;

namespace LiveLingo.Desktop.Web;

/// <param name="Theme">One of <see cref="SettingsViewModel.Themes"/>.</param>
internal sealed record GeneralSettingsState(
    string Hotkey, bool HotkeyInvalid, string CommitKey, string Language, string Theme, bool ReduceMotion, bool RememberChoices);

internal sealed record TermState(int I, string Source, string Target);

/// <param name="Microphones">Device names; the first, empty one is the system default.</param>
/// <param name="DefaultOutput">One of <see cref="SettingsViewModel.DefaultOutputs"/>.</param>
/// <param name="PolishPrompt">What the polish requirements box shows (the built-in text where nothing is customized).</param>
/// <param name="PolishPromptIsDefault">True while the built-in requirements are used.</param>
/// <param name="PolishPromptInvalid">The text the user typed is not usable and was not saved.</param>
internal sealed record VoiceSettingsState(
    IReadOnlyList<string> Microphones, string Microphone, string Direction, bool Cleanup, string DefaultOutput, bool AutoDictate,
    bool PreloadSpeech, IReadOnlyList<TermState> Terms, string PolishPrompt, bool PolishPromptIsDefault, bool PolishPromptInvalid);

/// <summary>One application the composer remembers a choice for; the summary reads like the output menu ("Translate to English").</summary>
internal sealed record AppPreferenceState(string Id, string Name, string Summary, string Used);

/// <summary>One saved cloud endpoint. There is no key here, only whether one is stored: the key itself never goes back to the page.</summary>
/// <param name="Host">What the address points at, for lists and questions where the whole URL is too long.</param>
/// <param name="Active">The one endpoint the cloud steps use; at most one is.</param>
internal sealed record EndpointState(string Id, string Name, string Url, string Host, string Model, bool HasKey, bool Active);

/// <param name="Form">The add or edit form while it is open, with why its last submission was refused.</param>
/// <param name="Change">A switch of the enabled endpoint or a deletion the user is being asked about.</param>
/// <param name="Consent">The step the user is being asked to authorize, or null.</param>
internal sealed record PrivacySettingsState(
    bool CloudTranslate, bool CloudClean, IReadOnlyList<EndpointState> Endpoints, EndpointForm? Form, PendingEndpointChange? Change,
    bool Invalid, CloudStep? Consent, IReadOnlyList<AppPreferenceState> Apps);

/// <param name="Text">What the old models are and how much room they take; empty when there are none.</param>
internal sealed record LegacyModelsState(int Count, string Text);

/// <summary>One line of the status the user can read out when asking for help; values never contain what the user wrote.</summary>
internal sealed record DiagnosticLine(string Label, string Value);

internal sealed record ModelsSettingsState(
    string Tier, LegacyModelsState Legacy, string Version, UpdateViewState Update, IReadOnlyList<DiagnosticLine> Diagnostics);

/// <summary>What the update row draws: a sentence, the download progress while there is one, and which buttons make sense.</summary>
/// <param name="Percent">The download progress; null when nothing is being downloaded.</param>
/// <param name="Busy">A check or download is under way; the page shows it and offers nothing else.</param>
/// <param name="CanDownload">The copy cannot update itself and the releases page is where to get a new installer.</param>
internal sealed record UpdateViewState(string Status, int? Percent, bool Busy, bool CanCheck, bool CanRestart, bool CanDownload)
{
    public static UpdateViewState From(UpdateState state, bool hasDownloadPage, SettingsTexts texts, string current) => new(
        texts.UpdateStatus(state, current),
        state.Phase == UpdatePhase.Downloading ? state.Percent : null,
        state.Busy,
        state.Phase is UpdatePhase.Idle or UpdatePhase.UpToDate or UpdatePhase.Failed,
        state.Phase == UpdatePhase.Ready,
        state.Phase == UpdatePhase.Unsupported && hasDownloadPage);
}

/// <summary>What the host knows about itself: the app version and the system it runs on.</summary>
internal sealed record AppInfo(string Version, string Platform);

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
    public static SettingsViewState From(SettingsViewModel vm, SettingsTexts texts, string section, AppInfo app, UpdateViewState update)
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
                vm.ReduceMotion,
                vm.RememberChoices),
            new VoiceSettingsState(
                vm.MicrophoneNames,
                vm.MicrophoneNames[vm.MicrophoneIndex],
                SettingsViewModel.Targets[vm.TargetIndex],
                vm.CleanupEnabled,
                SettingsViewModel.DefaultOutputs[vm.DefaultOutputIndex],
                vm.AutoDictate,
                vm.PreloadSpeech,
                [.. vm.Terms.Select((term, i) => new TermState(i, term.Source, term.Target))],
                vm.PolishPromptShown,
                vm.PolishPromptIsDefault,
                vm.PolishPromptInvalid),
            new PrivacySettingsState(
                vm.CloudTranslate, vm.CloudClean,
                [.. vm.Endpoints.Select(endpoint => new EndpointState(
                    endpoint.Id, endpoint.Name, endpoint.BaseUrl, HostOf(endpoint.BaseUrl), endpoint.Model, endpoint.HasKey, endpoint.Active))],
                vm.Form, vm.PendingChange, vm.CloudInvalid, vm.PendingConsent,
                [.. vm.Apps.Select(app => new AppPreferenceState(app.AppId, texts.AppName(app.Name), texts.AppSummary(app.Output, app.Target), texts.Date(app.UsedAt)))]),
            new ModelsSettingsState(
                SettingsViewModel.Tiers[vm.TierIndex],
                new LegacyModelsState(vm.LegacyCount, vm.HasLegacyModels ? texts.LegacyDescription(vm.LegacyCount, vm.LegacyBytes) : ""),
                app.Version,
                update,
                Diagnostics(vm, texts, app)),
            new SettingsOptionsState(!vm.ReduceMotion, theme));
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : url;

    /// <summary>Only settings that are switches or closed choices: terms, endpoints, device names and keys never go in.</summary>
    private static DiagnosticLine[] Diagnostics(SettingsViewModel vm, SettingsTexts texts, AppInfo app) =>
    [
        new(texts.Label("version"), app.Version),
        new(texts.Label("diag.platform"), app.Platform),
        new(texts.Label("language"), SettingsViewModel.Languages[vm.LanguageIndex]),
        new(texts.Label("model"), SettingsViewModel.Tiers[vm.TierIndex]),
        new(texts.Label("cleanup"), texts.OnOff(vm.CleanupEnabled)),
        new(texts.Label("cloud.translate"), texts.OnOff(vm.CloudTranslate)),
        new(texts.Label("cloud.clean"), texts.OnOff(vm.CloudClean)),
        new(texts.Label("cloud.endpoints"), vm.Endpoints.Count.ToString(CultureInfo.InvariantCulture)),
        new(texts.Label("cloud.apiKey"), texts.Stored(vm.ActiveEndpoint?.HasKey == true)),
        new(texts.Label("legacy"), vm.LegacyCount.ToString(CultureInfo.InvariantCulture))
    ];
}
