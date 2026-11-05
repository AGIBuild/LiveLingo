using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Preferences;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Tests.Settings;
using LiveLingo.Desktop.Web;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Web;

/// <summary>
/// The settings states, produced by driving the real view model. The golden fixtures the page tests render, like the
/// composer's.
/// </summary>
internal static class SettingsScenarios
{
    public static IReadOnlyDictionary<string, Func<Task<SettingsViewState>>> All { get; } = new SortedDictionary<string, Func<Task<SettingsViewState>>>
    {
        ["settings-general"] = () => Open(),
        ["settings-dark"] = () => Open(model: m =>
        {
            m.Appearance.Theme = "Dark";
            m.Appearance.Animations = false;
            m.Composer.CommitKey = "CtrlEnter";
        }),
        ["settings-hotkey-invalid"] = () => Open(then: f => f.Vm.SummonHotkey = "Ctrl+"),
        ["settings-voice"] = () => Open("voice", model: m =>
        {
            m.Composer.Microphone = "USB Mic";
            m.Composer.CleanupEnabled = false;
            m.Composer.Glossary = "Redis\n缓存穿透=cache penetration";
        }),
        ["settings-polish-custom"] = () => Open("voice", model: m =>
            m.Composer.PolishPrompt = "保留我的口语风格，修正明显错字，按完整意思分段，不要添加信息。"),
        ["settings-polish-invalid"] = () => Open("voice", then: f => f.Vm.PolishPrompt = "bad"),
        ["settings-privacy"] = () => Open("privacy"),
        ["settings-apps"] = () => Open("privacy", remembered: new PreferencesData
        {
            Apps =
            [
                new AppChoice("com.tinyspeck.slackmacgap", "Slack", ComposerOutput.Translate, TargetLanguage.English, new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)),
                new AppChoice("com.microsoft.VSCode", "Code", ComposerOutput.Clean, TargetLanguage.English, new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)),
                new AppChoice(ComposerPreferences.ClipboardId, "", ComposerOutput.Translate, TargetLanguage.Chinese, new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
            ]
        }),
        ["settings-cloud"] = () => Open("privacy", Cloud, key: "sk-stored"),
        ["settings-consent"] = () => Open("privacy", Cloud, key: "sk-stored", then: f => f.Vm.SetCloud(CloudStep.Clean, on: true)),
        ["settings-cloud-invalid"] = () => Open("privacy", then: f => f.Vm.SetCloud(CloudStep.Translate, on: true)),
        ["settings-endpoint-add"] = () => Open("privacy", then: f => f.Vm.OpenEndpointForm(null)),
        ["settings-endpoint-edit"] = () => Open("privacy", Cloud, key: "sk-stored", then: f => f.Vm.OpenEndpointForm("main")),
        ["settings-endpoint-error"] = () => Open("privacy", Cloud, key: "sk-stored", then: f =>
        {
            f.Vm.OpenEndpointForm(null);
            f.Vm.SaveEndpointAsync("Contoso", "http://api.example.com/v1", "m", "sk").GetAwaiter().GetResult();
        }),
        ["settings-endpoint-switch"] = () => Open("privacy", Cloud, key: "sk-stored", then: f => f.Vm.SetEndpointEnabled("other", on: true)),
        ["settings-endpoint-delete"] = () => Open("privacy", Cloud, key: "sk-stored", then: f => f.Vm.DeleteEndpoint("other")),
        ["settings-diagnostics"] = () => Open("models", model: m =>
        {
            Cloud(m);
            m.Composer.Glossary = "Redis\n缓存穿透=cache penetration";
            m.Composer.Microphone = "USB Mic";
        }, key: "sk-stored"),
        ["settings-models"] = () => Open("models", before: f => f.Models.ListInstalled().Returns([Legacy("old-a", 5_500_000_000), Legacy("old-b", 3_000_000_000)])),
        ["settings-update-checking"] = () => Open("models", update: new UpdateState(UpdatePhase.Checking)),
        ["settings-update-downloading"] = () => Open("models", update: new UpdateState(UpdatePhase.Downloading, "2.2.0", 42)),
        ["settings-update-ready"] = () => Open("models", update: new UpdateState(UpdatePhase.Ready, "2.2.0")),
        ["settings-update-latest"] = () => Open("models", update: new UpdateState(UpdatePhase.UpToDate, At: CheckedAt)),
        ["settings-update-failed"] = () => Open("models", update: new UpdateState(UpdatePhase.Failed, At: CheckedAt, Error: "Unable to connect to github.com")),
        ["settings-update-unsupported"] = () => Open("models", update: new UpdateState(UpdatePhase.Unsupported), downloadPage: true),
        ["settings-update-off"] = () => Open("models", update: UpdateState.Off),
    };

    public static AppInfo App { get; } = new("2.0.0", "macOS 15.0 Arm64");

    // Built from a local time so the text does not depend on the time zone the tests run in.
    private static DateTimeOffset CheckedAt { get; } = new(new DateTime(2026, 11, 3, 19, 5, 0, DateTimeKind.Local));

    public static SettingsViewState State(SettingsViewModel vm, string section = "general", UpdateState? update = null, bool downloadPage = false) =>
        SettingsViewState.From(
            vm, Texts.ChineseSettings(), section, App,
            UpdateViewState.From(update ?? new UpdateState(UpdatePhase.Idle), downloadPage, Texts.ChineseSettings(), App.Version)) with { Rev = 1 };

    private static void Cloud(SettingsModel model) => model.Cloud = CloudTestData.WithEndpoints(translate: true).Cloud;

    private static InstalledModel Legacy(string id, long bytes) =>
        new(id, id, "/models/" + id, bytes, ModelType.Translation, DateTime.UnixEpoch);

    /// <param name="before">Sets up what the system provides; the page reads it when it opens.</param>
    /// <param name="then">Acts on the opened settings, like the user.</param>
    private static async Task<SettingsViewState> Open(
        string section = "general",
        Action<SettingsModel>? model = null,
        string? key = null,
        PreferencesData? remembered = null,
        Action<SettingsViewModelTests.Fixture>? before = null,
        Action<SettingsViewModelTests.Fixture>? then = null,
        UpdateState? update = null,
        bool downloadPage = false)
    {
        var current = new SettingsModel();
        model?.Invoke(current);
        var f = new SettingsViewModelTests.Fixture(current, key, remembered);
        before?.Invoke(f);
        await f.Vm.InitializeAsync();
        then?.Invoke(f);
        return State(f.Vm, section, update, downloadPage);
    }
}
