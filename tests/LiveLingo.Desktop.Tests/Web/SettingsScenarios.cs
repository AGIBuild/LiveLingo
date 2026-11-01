using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Configuration;
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
        ["settings-privacy"] = () => Open("privacy"),
        ["settings-cloud"] = () => Open("privacy", Cloud, key: "sk-stored"),
        ["settings-consent"] = () => Open("privacy", Cloud, key: "sk-stored", then: f => f.Vm.SetCloud(CloudStep.Clean, on: true)),
        ["settings-cloud-invalid"] = () => Open("privacy", then: f =>
        {
            f.Vm.SetCloud(CloudStep.Translate, on: true);
            f.Vm.AnswerConsent(allow: true);
        }),
        ["settings-diagnostics"] = () => Open("models", model: m =>
        {
            Cloud(m);
            m.Composer.Glossary = "Redis\n缓存穿透=cache penetration";
            m.Composer.Microphone = "USB Mic";
        }, key: "sk-stored"),
        ["settings-models"] = () => Open("models", before: f => f.Models.ListInstalled().Returns([Legacy("old-a", 5_500_000_000), Legacy("old-b", 3_000_000_000)])),
    };

    public static AppInfo App { get; } = new("2.0.0", "macOS 15.0 Arm64");

    public static SettingsViewState State(SettingsViewModel vm, string section = "general") =>
        SettingsViewState.From(vm, Texts.ChineseSettings(), section, App) with { Rev = 1 };

    private static void Cloud(SettingsModel model)
    {
        model.Cloud.BaseUrl = "https://api.example.com/v1";
        model.Cloud.Model = "gpt-deploy";
        model.Cloud.Translate = true;
    }

    private static InstalledModel Legacy(string id, long bytes) =>
        new(id, id, "/models/" + id, bytes, ModelType.Translation, DateTime.UnixEpoch);

    /// <param name="before">Sets up what the system provides; the page reads it when it opens.</param>
    /// <param name="then">Acts on the opened settings, like the user.</param>
    private static async Task<SettingsViewState> Open(
        string section = "general",
        Action<SettingsModel>? model = null,
        string? key = null,
        Action<SettingsViewModelTests.Fixture>? before = null,
        Action<SettingsViewModelTests.Fixture>? then = null)
    {
        var current = new SettingsModel();
        model?.Invoke(current);
        var f = new SettingsViewModelTests.Fixture(current, key);
        before?.Invoke(f);
        await f.Vm.InitializeAsync();
        then?.Invoke(f);
        return State(f.Vm, section);
    }
}
