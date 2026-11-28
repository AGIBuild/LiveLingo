using System.Runtime.CompilerServices;
using System.Text.Json;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class SettingsViewStateTests
{
    public static TheoryData<string> Names => [.. SettingsScenarios.All.Keys];

    private const string StringsFixture = "settings-strings";

    /// <summary>The contract between C# and the settings page, as files; same rules as the composer's golden files.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task Snapshot_MatchesItsGoldenFile(string name)
    {
        var json = JsonSerializer.Serialize(await SettingsScenarios.All[name](), WebJson.Fixture) + "\n";
        var path = Path.Combine(FixtureDirectory(), name + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1")
            File.WriteAllText(path, json);

        Assert.True(File.Exists(path), $"Missing fixture {name}.json; run with UPDATE_FIXTURES=1.");
        Assert.Equal(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), json);
    }

    [Fact]
    public async Task Strings_MatchTheirGoldenFile()
    {
        var json = JsonSerializer.Serialize(
            new StringsData("zh-CN", Texts.ChineseSettings().WebStrings()), WebJson.Fixture) + "\n";
        var path = Path.Combine(FixtureDirectory(), StringsFixture + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1")
            File.WriteAllText(path, json);

        Assert.Equal(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), json);
    }

    [Fact]
    public void EverySettingsFixtureFile_HasAScenario()
    {
        var files = Directory.GetFiles(FixtureDirectory(), "settings-*.json").Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != StringsFixture);

        Assert.Equal(SettingsScenarios.All.Keys.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLabelIsTranslated_InBothLanguages()
    {
        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            var loc = new LocalizationService();
            loc.SetCulture(culture);
            var strings = new SettingsTexts(loc).WebStrings();

            Assert.All(strings, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"{culture} {pair.Key}"));
            Assert.DoesNotContain(strings.Values, value => value.StartsWith("settings.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheGroupsStartOnTheCurrentSettings()
    {
        var general = await SettingsScenarios.All["settings-dark"]();

        Assert.Equal("general", general.Section);
        Assert.Equal(("CtrlEnter", "Dark", true), (general.General.CommitKey, general.General.Theme, general.General.ReduceMotion));
        Assert.Equal(new SettingsOptionsState(Animations: false, Theme: "Dark"), general.Options);
    }

    [Fact]
    public void EveryUpdatePhase_HasATextInBothLanguages()
    {
        var done = new DateTimeOffset(2026, 11, 3, 11, 5, 0, TimeSpan.Zero);
        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            var loc = new LocalizationService();
            loc.SetCulture(culture);
            var texts = new SettingsTexts(loc);

            foreach (var phase in Enum.GetValues<UpdatePhase>())
            {
                var state = new UpdateState(phase, "2.2.0", 7, done, "offline");
                var text = texts.UpdateStatus(state, "2.1.0");

                Assert.False(string.IsNullOrWhiteSpace(text), $"{culture} {phase}");
                Assert.DoesNotContain("settings.update", text, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(UpdatePhase.Idle, false, "check")]
    [InlineData(UpdatePhase.UpToDate, false, "check")]
    [InlineData(UpdatePhase.Failed, false, "check")]
    [InlineData(UpdatePhase.Checking, false, "busy")]
    [InlineData(UpdatePhase.Downloading, false, "busy")]
    [InlineData(UpdatePhase.Ready, false, "restart")]
    [InlineData(UpdatePhase.Unsupported, true, "download")]
    [InlineData(UpdatePhase.Unsupported, false, "")]
    [InlineData(UpdatePhase.Off, true, "")]
    public void TheUpdateRow_OffersWhatTheStateAllows(UpdatePhase phase, bool page, string offered)
    {
        var view = UpdateViewState.From(new UpdateState(phase, "2.2.0", 5, DateTimeOffset.UnixEpoch, "x"), page, Texts.ChineseSettings(), "2.1.0");

        var actual = new[] { (view.CanCheck, "check"), (view.Busy, "busy"), (view.CanRestart, "restart"), (view.CanDownload, "download") }
            .Where(flag => flag.Item1).Select(flag => flag.Item2);
        Assert.Equal(offered == "" ? [] : [offered], actual);
        Assert.Equal(phase == UpdatePhase.Downloading ? 5 : null, view.Percent);
    }

    [Fact]
    public async Task AnInvalidHotkey_IsFlagged_AndKeepsWhatWasTyped()
    {
        var state = await SettingsScenarios.All["settings-hotkey-invalid"]();

        Assert.True(state.General.HotkeyInvalid);
        Assert.Equal("Ctrl+", state.General.Hotkey);
    }

    [Fact]
    public async Task Diagnostics_ListTheStateOfTheAppAndNothingTheUserWrote()
    {
        var state = await SettingsScenarios.All["settings-diagnostics"]();

        Assert.Equal("about", state.Section);
        var lines = state.About.Diagnostics.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("2.0.0", lines["版本"]);
        Assert.Equal("macOS 15.0 Arm64", lines["系统"]);
        Assert.Equal("关", lines["云端识别"]);
        Assert.Equal("开", lines["云端翻译"]);
        Assert.Equal("关", lines["云端整理"]);
        Assert.Equal("已保存", lines["API 密钥"]);

        // The terms, the endpoint, the deployment, the device and the key are the user's own; none of it may show.
        var all = string.Join('\n', lines.Select(l => l.Key + l.Value));
        foreach (var secret in new[] { "Redis", "缓存穿透", "api.example.com", "gpt-deploy", "USB Mic", "sk-stored" })
            Assert.DoesNotContain(secret, all);
    }

    [Fact]
    public async Task TheVoiceGroup_ListsTheDevicesAndTheTerms()
    {
        var state = await SettingsScenarios.All["settings-voice"]();

        Assert.Equal(["", "USB Mic", "Built-in"], state.Voice.Microphones);
        Assert.Equal("USB Mic", state.Voice.Microphone);
        Assert.False(state.Voice.Cleanup);
        Assert.Equal([new TermState(0, "Redis", "Redis"), new TermState(1, "缓存穿透", "cache penetration")], state.Voice.Terms);
    }

    [Fact]
    public async Task TheKey_IsNeverInTheSnapshot_OnlyWhetherOneIsStored()
    {
        var state = await SettingsScenarios.All["settings-cloud"]();
        var json = JsonSerializer.Serialize(state, WebJson.Wire);

        var cloudOptions = state.Models.Stages.Single(g => g.Stage == "translator").CloudOptions;
        Assert.All(cloudOptions, endpoint => Assert.True(endpoint.HasKey));
        Assert.DoesNotContain("sk-stored", json);
    }

    [Fact]
    public async Task TheEndpoints_AreListedInlineInModelsWithCrossStageBadges_AndPrivacyStripReflectsCloudState()
    {
        var state = await SettingsScenarios.All["settings-cloud"]();
        var translator = state.Models.Stages.Single(g => g.Stage == "translator");

        Assert.True(state.Models.Hardware.AnyCloud);
        Assert.Contains("云端整理", state.Models.Hardware.PrivacyStatus);
        Assert.Contains("云端翻译", state.Models.Hardware.PrivacyStatus);
        Assert.Equal(["Main", "Other"], translator.CloudOptions.Select(endpoint => endpoint.Name));
        Assert.Equal(["main"], translator.CloudOptions.Where(endpoint => endpoint.Active).Select(endpoint => endpoint.Id));
        Assert.Contains("整理", translator.CloudOptions[0].PeerBadge);
        Assert.Equal("other.example.com", translator.CloudOptions[1].Host);
        Assert.Null(state.Models.Form);
        Assert.Null(state.Models.DeleteConfirm);
    }

    [Fact]
    public async Task TheFormAndTheQuestionsAboutAnEndpoint_TravelInTheModelsSnapshot()
    {
        var add = await SettingsScenarios.All["settings-endpoint-add"]();
        var edit = await SettingsScenarios.All["settings-endpoint-edit"]();
        var error = await SettingsScenarios.All["settings-endpoint-error"]();
        var switching = await SettingsScenarios.All["settings-endpoint-switch"]();
        var delete = await SettingsScenarios.All["settings-endpoint-delete"]();

        Assert.Equal(new EndpointForm(null, null), add.Models.Form);
        Assert.Equal(new EndpointForm("main", null), edit.Models.Form);
        Assert.Equal(new EndpointForm(null, EndpointError.UrlInvalid), error.Models.Form);
        Assert.Equal(new ConsentModalState(CloudStep.Translate, "other", "Other", "other.example.com", "Main"), switching.Models.Consent);
        Assert.Equal("main", delete.Models.DeleteConfirm?.Id);
        Assert.Contains("云端翻译", delete.Models.DeleteConfirm?.AffectedSteps);

        // When cloud translation is active ("main"), only the cloud option is Active; no local translator radio card is marked Active.
        var translatorGroup = switching.Models.Stages.Single(g => g.Stage == "translator");
        Assert.True(translatorGroup.CloudRouted);
        Assert.Equal(["main"], translatorGroup.CloudOptions.Where(c => c.Active).Select(c => c.Id));
        Assert.DoesNotContain(translatorGroup.Options, o => o.Active);
    }

    [Fact]
    public async Task AskingForConsent_NamesTheStepAndTargetEndpoint()
    {
        var consent = await SettingsScenarios.All["settings-consent"]();
        var cloud = await SettingsScenarios.All["settings-cloud"]();

        Assert.Equal(new ConsentModalState(CloudStep.Clean, "main", "Main", "api.example.com", null), consent.Models.Consent);
        Assert.False(consent.Models.Stages.Single(g => g.Stage == "cleaner").CloudRouted);
        Assert.Null(cloud.Models.Consent);
        Assert.True(cloud.Models.Stages.Single(g => g.Stage == "translator").CloudRouted);
    }

    [Fact]
    public async Task ACloudStepThatCannotRun_IsFlagged()
    {
        var state = await SettingsScenarios.All["settings-cloud-invalid"]();
        var translator = state.Models.Stages.Single(g => g.Stage == "translator");

        Assert.True(translator.CloudInvalid);
        Assert.False(translator.CloudRouted);
    }

    [Fact]
    public async Task ModelsFromEarlierVersions_AreDescribedOnlyWhenThereAreSome()
    {
        var with = await SettingsScenarios.All["settings-models"]();
        var without = await SettingsScenarios.All["settings-general"]();

        Assert.Equal(2, with.Models.Storage.Legacy.Count);
        Assert.Contains("8.5", with.Models.Storage.Legacy.Text);
        Assert.Equal(new LegacyModelsState(0, ""), without.Models.Storage.Legacy);
    }

    [Fact]
    public async Task LocalStorageAndActiveDownloads_ReportExactSizesPathsAndProgress()
    {
        var models = await SettingsScenarios.All["settings-models"]();
        Assert.Equal("/models", models.Models.Storage.Path);
        Assert.Equal(7, models.Models.Storage.Items.Count);
        Assert.Contains(models.Models.Storage.Items, item => item.Id == "qwen3.5-2b-q4km" && !item.InUse && item.CanDelete && item.CanActivate);
        Assert.Contains(models.Models.Storage.Items, item => item.Id == "x-asr-480ms-streaming-int8" && item.InUse && !item.CanDelete);

        var downloading = await SettingsScenarios.All["settings-models-downloading"]();
        Assert.Equal("42%", downloading.Models.NavBadge);
        var activeTask = Assert.Single(downloading.Models.Downloads);
        Assert.Equal("hy-mt2-7b-q4km", activeTask.Id);
        Assert.Equal(42, activeTask.Percent);
        Assert.Contains("1.94 GB / 4.62 GB", activeTask.StatusText);
    }

    [Fact]
    public async Task TheRememberedApps_AreNamedAndSummarizedUnderGeneralInTheWordsOfTheOutputMenu()
    {
        var state = await SettingsScenarios.All["settings-apps"]();

        Assert.Equal("general", state.Section);
        Assert.Equal(["Slack", "Code", "仅复制"], state.General.Apps.Select(app => app.Name));
        Assert.Equal(["翻译成 English", "整理原话（保持原语言）", "翻译成 中文"], state.General.Apps.Select(app => app.Summary));
        Assert.Equal("com.tinyspeck.slackmacgap", state.General.Apps[0].Id);
    }

    [Fact]
    public async Task NoSnapshot_ContainsAnUnexpectedField()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(await SettingsScenarios.All["settings-cloud"](), WebJson.Wire));

        Assert.Equal(
            ["about", "general", "models", "options", "rev", "section", "voice"],
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["consent", "deleteConfirm", "downloads", "form", "hardware", "hotHost", "localHostRefreshing", "navBadge", "stages", "storage", "tab"],
            document.RootElement.GetProperty("models").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    private static string FixtureDirectory([CallerFilePath] string self = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "LiveLingo.Web.Tests", "Fixtures"));
}
