using System.Runtime.CompilerServices;
using System.Text.Json;
using LiveLingo.Desktop.Services.Localization;
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

        var lines = state.Models.Diagnostics.ToDictionary(l => l.Label, l => l.Value);
        Assert.Equal("2.0.0", lines["版本"]);
        Assert.Equal("macOS 15.0 Arm64", lines["系统"]);
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

        Assert.True(state.Privacy.HasKey);
        Assert.DoesNotContain("sk-stored", json);
    }

    [Fact]
    public async Task AskingForConsent_NamesTheStep()
    {
        var consent = await SettingsScenarios.All["settings-consent"]();
        var cloud = await SettingsScenarios.All["settings-cloud"]();

        Assert.Equal(CloudStep.Clean, consent.Privacy.Consent);
        Assert.False(consent.Privacy.CloudClean);
        Assert.Null(cloud.Privacy.Consent);
        Assert.True(cloud.Privacy.CloudTranslate);
    }

    [Fact]
    public async Task ACloudStepThatCannotRun_IsFlagged()
    {
        var state = await SettingsScenarios.All["settings-cloud-invalid"]();

        Assert.True(state.Privacy.Invalid);
        Assert.False(state.Privacy.CloudTranslate);
    }

    [Fact]
    public async Task ModelsFromEarlierVersions_AreDescribedOnlyWhenThereAreSome()
    {
        var with = await SettingsScenarios.All["settings-models"]();
        var without = await SettingsScenarios.All["settings-privacy"]();

        Assert.Equal(2, with.Models.Legacy.Count);
        Assert.Contains("8.5", with.Models.Legacy.Text);
        Assert.Equal(new LegacyModelsState(0, ""), without.Models.Legacy);
    }

    [Fact]
    public async Task NoSnapshot_ContainsAnUnexpectedField()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(await SettingsScenarios.All["settings-cloud"](), WebJson.Wire));

        Assert.Equal(
            ["general", "models", "options", "privacy", "rev", "section", "voice"],
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["baseUrl", "cloudClean", "cloudTranslate", "consent", "hasKey", "invalid", "model"],
            document.RootElement.GetProperty("privacy").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    private static string FixtureDirectory([CallerFilePath] string self = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "LiveLingo.Web.Tests", "Fixtures"));
}
