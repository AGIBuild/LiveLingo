using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;

namespace LiveLingo.Desktop.Tests.Web;

public sealed class ComposerIntentsTests
{
    private sealed class Rig : IAsyncDisposable
    {
        public Harness H { get; }
        public RecentDrafts Recent { get; } = new();
        public FakeFiles Files { get; } = new();
        public ListLogger Log { get; } = new();
        public ComposerIntents Sut { get; }

        public Rig(ComposerOutput output = ComposerOutput.Translate)
        {
            H = new Harness(output: output);
            Sut = new ComposerIntents(H.Vm, Recent, Files, Log);
        }

        public ComposerViewModel Vm => H.Vm;

        public void Do(WebIntent intent) => Sut.Handle(intent);

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }

    [Fact]
    public async Task Source_SetsTheTextAndRemembersThePagesRevision()
    {
        await using var rig = new Rig();

        rig.Do(new SourceIntent("你好", 5) { V = 1 });

        Assert.Equal("你好", rig.Vm.SourceText);
        Assert.Equal(5, rig.Sut.SourceRev);
    }

    [Fact]
    public async Task TextChangedByTheModel_IsNewerThanAnythingThePageSent()
    {
        await using var rig = new Rig();
        rig.Do(new SourceIntent("typed", 7) { V = 1 });

        rig.Vm.SourceText = "spoken";

        Assert.Equal(8, rig.Sut.SourceRev);
    }

    [Fact]
    public async Task TextTypedInThePage_DoesNotBumpTheRevisionItself()
    {
        await using var rig = new Rig();

        rig.Do(new SourceIntent("a", 2) { V = 1 });
        rig.Do(new SourceIntent("ab", 3) { V = 1 });

        Assert.Equal(3, rig.Sut.SourceRev);
    }

    [Fact]
    public async Task AFreshPage_ShowsAnAlreadyRestoredDraft()
    {
        await using var rig = new Rig();

        // A page that has sent nothing starts at revision 0; the model's first revision is above it.
        Assert.True(rig.Sut.SourceRev > 0);
    }

    [Fact]
    public async Task Size_IsPassedOnToTheWindow()
    {
        await using var rig = new Rig();
        SizeIntent? seen = null;
        rig.Sut.SizeChanged += size => seen = size;

        rig.Do(new SizeIntent(656, 321) { V = 1 });

        Assert.Equal((656, 321), (seen!.Width, seen.Height));
    }

    [Fact]
    public async Task AnOutputThatIsNotOffered_IsRefused()
    {
        await using var h = new Harness(output: null, options: ComposerOptions.Default with { Cleanup = false });
        var sut = new ComposerIntents(h.Vm, new RecentDrafts(), new FakeFiles(), new ListLogger());

        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Handle(new OutputIntent(ComposerOutput.Clean) { V = 1 }));
        Assert.Equal(ComposerOutput.Original, h.Vm.Output);
    }

    [Fact]
    public async Task Output_Swap_AndTheThreeSwitches_ReachTheViewModel()
    {
        await using var rig = new Rig();
        var target = rig.Vm.TargetLanguage;

        rig.Do(new OutputIntent(ComposerOutput.Original) { V = 1 });
        rig.Do(new SwapLanguageIntent { V = 1 });
        rig.Do(new WideIntent(true) { V = 1 });
        rig.Do(new WholeIntent(true) { V = 1 });
        rig.Do(new CopyOnlyIntent(true) { V = 1 });

        Assert.Equal(ComposerOutput.Original, rig.Vm.Output);
        Assert.NotEqual(target, rig.Vm.TargetLanguage);
        Assert.True(rig.Vm.ForceWide);
        Assert.True(rig.Vm.ForceWhole);
        Assert.True(rig.Vm.CopyOnly);
    }

    [Fact]
    public async Task Commit_WritesWhatTheViewModelDecidesToWrite()
    {
        await using var rig = new Rig(ComposerOutput.Original);
        rig.Do(new SourceIntent("hello", 1) { V = 1 });
        await Wait.Until(() => rig.Vm.CanWrite);

        rig.Do(new CommitIntent(true) { V = 1 });

        await Wait.Until(() => rig.H.Events.Contains("completed"));
    }

    [Fact]
    public async Task Hide_RequestsClose_AndSettingsRaiseTheirEvent()
    {
        await using var rig = new Rig();
        string? section = null;
        rig.Vm.SettingsRequested += s => section = s;

        rig.Do(new HideIntent { V = 1 });
        rig.Do(new OpenSettingsIntent("voice") { V = 1 });

        Assert.Contains("close", rig.H.Events);
        Assert.Equal("voice", section);
    }

    [Fact]
    public async Task Banner_RunsTheActionOfTheViewModel()
    {
        await using var rig = new Rig();
        rig.Vm.Banner = new BannerState(BannerKind.NoSpeechFound);

        rig.Do(new BannerIntent(BannerAction.Dismiss) { V = 1 });

        await Wait.Until(() => rig.Vm.Banner.Kind == BannerKind.None);
    }

    [Fact]
    public async Task Editing_GoesThroughTheViewModelsOwnCommands()
    {
        await using var rig = new Rig();
        rig.Do(new SourceIntent("Hello there.", 1) { V = 1 });
        await Wait.Until(() => rig.Vm.Lines is [{ State: LineState.Ready }]);

        rig.Do(new EditIntent(0) { V = 1 });
        Assert.Equal(0, rig.Vm.EditingIndex);
        rig.Do(new EditTextIntent("Hi!") { V = 1 });
        rig.Do(new SaveEditIntent { V = 1 });

        Assert.Equal(-1, rig.Vm.EditingIndex);
        Assert.Equal("Hi!", rig.Vm.Lines[0].Text);
        Assert.True(rig.Vm.Lines[0].IsOwned);
    }

    [Fact]
    public async Task CancelEdit_LeavesTheSentenceAlone()
    {
        await using var rig = new Rig();
        rig.Do(new SourceIntent("Hello there.", 1) { V = 1 });
        await Wait.Until(() => rig.Vm.Lines is [{ State: LineState.Ready }]);
        var before = rig.Vm.Lines[0].Text;

        rig.Do(new EditIntent(0) { V = 1 });
        rig.Do(new EditTextIntent("changed") { V = 1 });
        rig.Do(new CancelEditIntent { V = 1 });

        Assert.Equal(-1, rig.Vm.EditingIndex);
        Assert.Equal(before, rig.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task RecentDrafts_AreRestoredAndCleared()
    {
        await using var rig = new Rig();
        rig.Recent.Close("older draft", written: true);
        rig.Recent.Close("latest draft", written: true);
        var changed = 0;
        rig.Sut.Changed += () => changed++;

        rig.Do(new RestoreRecentIntent(1) { V = 1 });
        rig.Do(new RestoreRecentIntent(4) { V = 1 });
        Assert.Equal("older draft", rig.Vm.SourceText);

        rig.Do(new ClearRecentIntent { V = 1 });
        Assert.Empty(rig.Recent.Items);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Import_PicksAFile_AndACancelledPickDoesNothing()
    {
        await using var rig = new Rig();

        rig.Do(new ImportIntent { V = 1 });
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Empty(rig.H.Transcriber.Paths);

        rig.Files.Recording = "/tmp/a.wav";
        rig.Do(new ImportIntent { V = 1 });
        await Wait.Until(() => rig.H.Transcriber.Paths.Count == 1);
        Assert.Equal("/tmp/a.wav", rig.H.Transcriber.Paths[0]);
    }

    [Fact]
    public async Task Export_AsksWhereToSave_ThenWritesTheFile()
    {
        await using var rig = new Rig(ComposerOutput.Original);
        rig.Do(new SourceIntent("hello", 1) { V = 1 });
        await Wait.Until(() => rig.Vm.CanExportAs(ExportFormat.Txt));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        rig.Files.ExportPath = path;

        try
        {
            rig.Do(new ExportIntent(ExportFormat.Txt) { V = 1 });
            await Wait.Until(() => File.Exists(path));

            Assert.Equal([ExportFormat.Txt], rig.Files.Exports);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AFailingAction_IsLoggedByTypeOnly()
    {
        await using var rig = new Rig();
        rig.Files.Failure = new InvalidOperationException("secret draft text");

        rig.Do(new ImportIntent { V = 1 });
        await Wait.Until(() => rig.Log.Lines.Count == 1);

        Assert.Contains("InvalidOperationException", rig.Log.Lines[0]);
        Assert.DoesNotContain("secret", rig.Log.Lines[0]);
    }

    [Fact]
    public async Task AnIntentOutsideTheComposerSet_IsABug()
    {
        await using var rig = new Rig();

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Do(new ReadyIntent { V = 1 }));
    }
}
