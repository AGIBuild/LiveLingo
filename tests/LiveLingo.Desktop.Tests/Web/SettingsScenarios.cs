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
        ["settings-apps"] = () => Open("general", remembered: new PreferencesData
        {
            Apps =
            [
                new AppChoice("com.tinyspeck.slackmacgap", "Slack", ComposerOutput.Translate, TargetLanguage.English, new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)),
                new AppChoice("com.microsoft.VSCode", "Code", ComposerOutput.Clean, TargetLanguage.English, new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero)),
                new AppChoice(ComposerPreferences.ClipboardId, "", ComposerOutput.Translate, TargetLanguage.Chinese, new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))
            ]
        }),
        ["settings-cloud"] = () => Open("models", m => m.Cloud = CloudTestData.WithEndpoints(translatorId: "main", cleanerId: "main").Cloud, key: "sk-stored", modelsTab: "translator"),
        ["settings-consent"] = () => Open("models", Cloud, key: "sk-stored", modelsTab: "cleaner", then: f => f.Vm.SelectCloudEndpoint(CloudStep.Clean, "main")),
        ["settings-speech-consent"] = () => Open("models", Cloud, key: "sk-stored", modelsTab: "speech", then: f => f.Vm.SelectCloudEndpoint(CloudStep.Speech, "main")),
        ["settings-speech-endpoint-add"] = () => Open("models", modelsTab: "speech", then: f => f.Vm.OpenEndpointForm(null)),
        ["settings-cloud-invalid"] = () => Open("models", m => m.Cloud = CloudTestData.WithEndpoints().Cloud, key: null, modelsTab: "translator", then: f => f.Vm.SelectCloudEndpoint(CloudStep.Translate, "main")),
        ["settings-endpoint-add"] = () => Open("models", modelsTab: "translator", then: f => f.Vm.OpenEndpointForm(null)),
        ["settings-endpoint-edit"] = () => Open("models", Cloud, key: "sk-stored", modelsTab: "translator", then: f => f.Vm.OpenEndpointForm("main")),
        ["settings-endpoint-error"] = () => Open("models", Cloud, key: "sk-stored", modelsTab: "translator", then: f =>
        {
            f.Vm.OpenEndpointForm(null);
            f.Vm.SaveEndpointAsync("Contoso", "http://api.example.com/v1", "m", "sk").GetAwaiter().GetResult();
        }),
        ["settings-endpoint-switch"] = () => Open(
            "models",
            Cloud,
            key: "sk-stored",
            modelsTab: "translator",
            before: f => f.Models.ListInstalled().Returns([Legacy(ModelCatalog.TranslatorFast.Id, ModelCatalog.TranslatorFast.SizeBytes)]),
            then: f => f.Vm.SelectCloudEndpoint(CloudStep.Translate, "other")),
        ["settings-endpoint-delete"] = () => Open("models", Cloud, key: "sk-stored", modelsTab: "translator", then: f => f.Vm.DeleteEndpoint("main")),
        ["settings-diagnostics"] = () => Open("about", model: m =>
        {
            Cloud(m);
            m.Composer.Glossary = "Redis\n缓存穿透=cache penetration";
            m.Composer.Microphone = "USB Mic";
        }, key: "sk-stored"),
        ["settings-models"] = () => Open("models", before: f => f.Models.ListInstalled().Returns([
            Legacy(ModelCatalog.SpeechLive.Id, ModelCatalog.SpeechLive.SizeBytes),
            Legacy(ModelCatalog.SpeechSecondPass.Id, ModelCatalog.SpeechSecondPass.SizeBytes),
            Legacy(ModelCatalog.SpeechVad.Id, ModelCatalog.SpeechVad.SizeBytes),
            Legacy(ModelCatalog.LlamaRuntimeFor("osx-arm64").Id, ModelCatalog.LlamaRuntimeFor("osx-arm64").SizeBytes),
            Legacy(ModelCatalog.Cleaner.Id, ModelCatalog.Cleaner.SizeBytes),
            Legacy(ModelCatalog.CleanerFast.Id, ModelCatalog.CleanerFast.SizeBytes),
            Legacy(ModelCatalog.TranslatorFast.Id, ModelCatalog.TranslatorFast.SizeBytes),
            Legacy("old-a", 5_500_000_000),
            Legacy("old-b", 3_000_000_000)])),
        ["settings-models-downloading"] = () => Open(
            "models",
            model: m =>
            {
                m.Composer.SpeechModel = ModelCatalog.SpeechSecondPass.Id;
                m.Composer.CleanerModel = ModelCatalog.Cleaner.Id;
                m.Composer.TranslatorModel = ModelCatalog.TranslatorFast.Id;
            },
            hardware: new HardwareProfile("Apple M3 Pro", ComputeBackend.AppleMetal, 36L * 1024 * 1024 * 1024, 12),
            before: f =>
            {
                f.Models.ListInstalled().Returns([
                    Legacy(ModelCatalog.LlamaRuntimeFor("osx-arm64").Id, ModelCatalog.LlamaRuntimeFor("osx-arm64").SizeBytes),
                    Legacy(ModelCatalog.SpeechSecondPass.Id, ModelCatalog.SpeechSecondPass.SizeBytes),
                    Legacy(ModelCatalog.Cleaner.Id, ModelCatalog.Cleaner.SizeBytes),
                    Legacy(ModelCatalog.TranslatorFast.Id, ModelCatalog.TranslatorFast.SizeBytes)]);
                f.Downloads.GetState(ModelCatalog.TranslatorQuality.Id).Returns(new ModelDownloadState(
                    ModelCatalog.TranslatorQuality.Id,
                    ModelDownloadStatus.Downloading,
                    42,
                    null,
                    1_942_352_536,
                    ModelCatalog.TranslatorQuality.SizeBytes));
            },
            then: f => f.Vm.DownloadModel(ModelCatalog.TranslatorQuality.Id)),
        ["settings-update-checking"] = () => Open("about", update: new UpdateState(UpdatePhase.Checking)),
        ["settings-update-downloading"] = () => Open("about", update: new UpdateState(UpdatePhase.Downloading, "2.2.0", 42)),
        ["settings-update-ready"] = () => Open("about", update: new UpdateState(UpdatePhase.Ready, "2.2.0")),
        ["settings-update-latest"] = () => Open("about", update: new UpdateState(UpdatePhase.UpToDate, At: CheckedAt)),
        ["settings-update-failed"] = () => Open("about", update: new UpdateState(UpdatePhase.Failed, At: CheckedAt, Error: "Unable to connect to github.com")),
        ["settings-update-unsupported"] = () => Open("about", update: new UpdateState(UpdatePhase.Unsupported), downloadPage: true),
        ["settings-update-off"] = () => Open("about", update: UpdateState.Off),
    };

    public static AppInfo App { get; } = new("2.0.0", "macOS 15.0 Arm64");

    // Built from a local time so the text does not depend on the time zone the tests run in.
    private static DateTimeOffset CheckedAt { get; } = new(new DateTime(2026, 11, 3, 19, 5, 0, DateTimeKind.Local));

    public static SettingsViewState State(
        SettingsViewModel vm,
        string section = "general",
        UpdateState? update = null,
        bool downloadPage = false,
        string modelsTab = "speech") =>
        SettingsViewState.From(
            vm, Texts.ChineseSettings(), section, App,
            UpdateViewState.From(update ?? new UpdateState(UpdatePhase.Idle), downloadPage, Texts.ChineseSettings(), App.Version),
            modelsTab) with { Rev = 1 };

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
        bool downloadPage = false,
        HardwareProfile? hardware = null,
        string modelsTab = "speech")
    {
        var current = new SettingsModel();
        model?.Invoke(current);
        var f = new SettingsViewModelTests.Fixture(current, key, remembered, hardware);
        before?.Invoke(f);
        await f.Vm.InitializeAsync();
        then?.Invoke(f);
        return State(f.Vm, section, update, downloadPage, modelsTab);
    }
}
