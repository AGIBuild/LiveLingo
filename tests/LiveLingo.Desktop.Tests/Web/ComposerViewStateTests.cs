using System.Runtime.CompilerServices;
using System.Text.Json;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class ComposerViewStateTests
{
    public static TheoryData<string> Names => [.. ComposerScenarios.All.Keys];

    /// <summary>
    /// The contract between C# and the page, as files: the page tests render exactly these. A changed field fails here
    /// first. Set UPDATE_FIXTURES=1 to rewrite them after an intended change, and review the diff.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task Snapshot_MatchesItsGoldenFile(string name)
    {
        var json = JsonSerializer.Serialize(await ComposerScenarios.All[name](), WebJson.Fixture) + "\n";
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
            new StringsData("zh-CN", Texts.Chinese().WebStrings()), WebJson.Fixture) + "\n";
        var path = Path.Combine(FixtureDirectory(), StringsFixture + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1")
            File.WriteAllText(path, json);

        Assert.Equal(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), json);
    }

    [Fact]
    public void EveryFixtureFile_HasAScenario()
    {
        var files = Directory.GetFiles(FixtureDirectory(), "*.json").Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != StringsFixture && !name!.StartsWith("settings-", StringComparison.Ordinal));

        Assert.Equal(ComposerScenarios.All.Keys.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
    }

    private const string StringsFixture = "strings";

    [Theory]
    [InlineData("target-lost", "targetLost")]
    [InlineData("write-failed", "writeFailed")]
    [InlineData("mic-denied", "microphoneDenied")]
    [InlineData("no-microphone", "noMicrophone")]
    [InlineData("model-missing", "modelMissing")]
    [InlineData("downloading", "downloading")]
    [InlineData("download-failed", "downloadFailed")]
    [InlineData("slow", "slowProcessing")]
    [InlineData("no-speech", "noSpeechFound")]
    public async Task BannerScenarios_ShowTheirBanner(string name, string kind)
    {
        var state = await ComposerScenarios.All[name]();

        Assert.Equal(kind, state.Banner?.Kind);
        Assert.NotEmpty(state.Banner!.Message);
    }

    [Fact]
    public async Task NoBanner_IsNull()
    {
        Assert.Null((await ComposerScenarios.All["translate-ready"]()).Banner);
    }

    [Fact]
    public async Task TypedText_ShowsTheResultAndAllowsWriting()
    {
        var state = await ComposerScenarios.All["translate-ready"]();

        Assert.Equal("edit", state.Source.Mode);
        Assert.True(state.Result.Visible);
        Assert.Equal(2, state.Result.Rows.Count);
        Assert.All(state.Result.Rows, row => Assert.Equal("ready", row.State));
        Assert.True(state.Footer.Commit.Enabled);
        Assert.True(state.Footer.CopyEnabled);
        Assert.Equal("target", state.Dest.Kind);
        Assert.Equal("translate", state.Output.Value);
        Assert.True(state.Output.Globe);
        Assert.Equal(["clean", "translate", "original"], state.Output.Menu.Select(item => item.Id));
        Assert.Equal(["translate"], state.Output.Menu.Where(item => item.Checked).Select(item => item.Id));
    }

    [Fact]
    public async Task PendingText_ArmsTheCommit()
    {
        var pending = await ComposerScenarios.All["translate-pending"]();
        var armed = await ComposerScenarios.All["translate-armed"]();

        Assert.All(pending.Result.Rows, row => Assert.Equal("pending", row.State));
        Assert.True(pending.Footer.Commit.Enabled);
        Assert.False(pending.Footer.Commit.Armed);
        Assert.True(armed.Footer.Commit.Armed);
    }

    [Fact]
    public async Task AWrittenDraft_IsDone()
    {
        var state = await ComposerScenarios.All["written"]();

        Assert.True(state.Footer.Commit.Written);
        Assert.False(state.Footer.Commit.Enabled);
        Assert.False(state.Source.Editable);
    }

    [Fact]
    public async Task WithoutATarget_TheDestinationIsCopyOnly()
    {
        var state = await ComposerScenarios.All["copy-only"]();

        Assert.Equal("copyOnly", state.Dest.Kind);
        var target = state.Menus.Target.Single(item => item.Id == "target");
        Assert.False(target.Enabled);
        Assert.True(state.Menus.Target.Single(item => item.Id == "copyOnly").Checked);
    }

    [Fact]
    public async Task ALostTarget_IsMarked_AndCanBeChosenAgainOnlyWhenItExists()
    {
        var state = await ComposerScenarios.All["target-lost"]();

        Assert.Equal("lost", state.Dest.Kind);
        Assert.False(state.Menus.Target.Single(item => item.Id == "target").Enabled);
    }

    [Fact]
    public async Task AFailedSentence_CarriesItsErrorInsteadOfText()
    {
        var state = await ComposerScenarios.All["row-failed"]();

        Assert.Equal(["ready", "failed"], state.Result.Rows.Select(row => row.State));
        Assert.Equal("warn", state.Footer.Status.Tone);
    }

    [Fact]
    public async Task Editing_MarksTheRow_AndOffersItsText()
    {
        var state = await ComposerScenarios.All["editing"]();

        var row = Assert.Single(state.Result.Rows, row => row.Editing);
        Assert.Equal(0, row.I);
        Assert.NotEmpty(state.Result.EditText);
        Assert.All(state.Result.Rows, other => Assert.False(other.CanEdit));
    }

    [Fact]
    public async Task ACleanupThatKeptTheOriginal_ExplainsWhy()
    {
        var state = await ComposerScenarios.All["gate-warning"]();

        Assert.True(state.Result.Rows[0].Warn);
        Assert.Contains("丢了数字 8080", state.Result.Rows[0].Note);
    }

    [Fact]
    public async Task Cleanup_OffersUseOriginal_Translation_DoesNot()
    {
        var clean = await ComposerScenarios.All["clean-ready"]();
        var translate = await ComposerScenarios.All["translate-ready"]();

        Assert.All(clean.Result.Rows, row => Assert.True(row.CanUseOriginal));
        Assert.All(translate.Result.Rows, row => Assert.False(row.CanUseOriginal));
    }

    [Fact]
    public async Task Listening_ShowsLiveLines_AndARecordingMic()
    {
        var state = await ComposerScenarios.All["listening"]();

        Assert.Equal("live", state.Source.Mode);
        Assert.True(state.Footer.Mic.Recording);
        Assert.Contains(state.Source.Lines, line => line.Tail);
        Assert.False(state.Source.Editable);
    }

    [Fact]
    public async Task AnImportedRecording_IsATimelineAndAllowsSubtitles()
    {
        var state = await ComposerScenarios.All["timeline"]();

        Assert.Equal("live", state.Source.Mode);
        Assert.True(state.Layout.Wide);
        Assert.Equal(["0:00", "1:05", "2:10"], state.Source.Lines.Select(line => line.Time));
        Assert.True(state.Dialogs.Export.Srt);
        Assert.Contains("weekly.wav", state.Source.Label);
    }

    [Fact]
    public async Task AWholeDocument_UsesTheWideLayoutAndShowsItsRoute()
    {
        var state = await ComposerScenarios.All["whole-wide"]();

        Assert.True(state.Layout.Wide);
        Assert.True(state.Source.RouteChip);
        Assert.True(state.Menus.More.Single(item => item.Id == "whole").Checked);
    }

    [Fact]
    public async Task AnEmptyComposer_ShowsTheHint()
    {
        var state = await ComposerScenarios.All["empty"]();

        Assert.True(state.Source.Hint);
        Assert.False(state.Result.Visible);
        Assert.False(state.Footer.Commit.Enabled);
        Assert.False(state.Dialogs.Export.Srt);
    }

    [Fact]
    public async Task Options_CarryTheCloudChipAndTheCommitKey()
    {
        var state = await ComposerScenarios.All["cloud-ctrl-enter"]();
        var still = await ComposerScenarios.All["reduced-motion"]();

        Assert.True(state.Privacy.Cloud);
        Assert.Equal("ctrlEnter", state.Options.CommitKey);
        Assert.True(state.Options.Animations);
        Assert.False(still.Options.Animations);
        Assert.False(still.Privacy.Cloud);
    }

    [Fact]
    public async Task WithoutCleanup_TheOutputMenuHasNoCleanEntry()
    {
        var all = await ComposerScenarios.All["original-ready"]();
        var none = await ComposerScenarios.All["no-cleanup"]();

        Assert.Equal(["clean", "translate", "original"], all.Output.Menu.Select(m => m.Id));
        Assert.Equal(["translate", "original"], none.Output.Menu.Select(m => m.Id));
        Assert.Equal("original", none.Output.Value);
    }

    [Fact]
    public async Task RecentDrafts_AreListedNewestFirst_AndRestorableOnlyWhenIdle()
    {
        await using var h = new Harness();
        var recent = new RecentDrafts();
        recent.Close("first draft", written: true);
        recent.Close("second draft", written: false);

        var idle = ComposerScenarios.State(h, recent);
        h.Vm.SourceText = "typing now";
        var busy = ComposerScenarios.State(h, recent);

        Assert.Equal([0, 1], idle.Dialogs.Recent.Select(item => item.Index));
        Assert.Contains("second draft", idle.Dialogs.Recent[0].Label);
        Assert.True(idle.Dialogs.CanRestore);
        Assert.False(busy.Dialogs.CanRestore);
    }

    [Fact]
    public async Task ImportingIsOnlyOfferedToAnIdleComposer()
    {
        var empty = await ComposerScenarios.All["empty"]();
        var typed = await ComposerScenarios.All["translate-ready"]();

        Assert.True(empty.Menus.More.Single(item => item.Id == "import").Enabled);
        Assert.False(typed.Menus.More.Single(item => item.Id == "import").Enabled);
    }

    [Fact]
    public async Task TheSourceRevision_IsWhatTheCallerPassed()
    {
        await using var h = new Harness();

        var state = ComposerViewState.From(h.Vm, Texts.Chinese(), new RecentDrafts(), sourceRev: 42);

        Assert.Equal(42, state.Source.SourceRev);
    }

    [Fact]
    public async Task NoSnapshot_ContainsTheWordsOfAnUnexpectedField()
    {
        // The snapshot is plain data of the closed shape above; a stray field would be a secret or internal leak.
        var json = JsonSerializer.Serialize(await ComposerScenarios.All["translate-ready"](), WebJson.Wire);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            ["banner", "dest", "dialogs", "footer", "layout", "menus", "options", "output", "privacy", "result", "rev", "source"],
            document.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void StringsCoverTheStaticLabelsOfThePage()
    {
        var strings = Texts.Chinese().WebStrings();

        Assert.All(strings, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.DoesNotContain(strings.Values, value => value.StartsWith("composer.", StringComparison.Ordinal));
    }

    private static string FixtureDirectory([CallerFilePath] string self = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "LiveLingo.Web.Tests", "Fixtures"));
}
