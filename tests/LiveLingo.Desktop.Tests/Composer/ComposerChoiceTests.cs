using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Tests.Composer;

/// <summary>The composer's side of the remembered choices: where it begins, what counts as a choice, and going back.</summary>
public class ComposerChoiceTests
{
    private static ComposerStart Start(
        ComposerOutput output = ComposerOutput.Translate, TargetLanguage target = TargetLanguage.English,
        bool applied = true, string? appId = "com.slack") =>
        new(output, target, ComposerOutput.Clean, TargetLanguage.English, appId is null ? null : new RememberContext(appId, "Slack", applied));

    [Fact]
    public async Task Begin_SetsTheOutputAndTheDirection_WithoutCountingAsAChoice()
    {
        await using var h = new Harness(output: ComposerOutput.Clean);
        var choices = new List<ComposerChoice>();
        h.Vm.ChoiceMade += choices.Add;

        h.Vm.Begin(Start(ComposerOutput.Translate, TargetLanguage.Chinese));

        Assert.Equal((ComposerOutput.Translate, TargetLanguage.Chinese), (h.Vm.Output, h.Vm.TargetLanguage));
        Assert.Empty(choices);
    }

    [Fact]
    public async Task ChoosingAnOutput_IsAChoice_AndCarriesTheDirectionInForce()
    {
        await using var h = new Harness();
        var choices = new List<ComposerChoice>();
        h.Vm.ChoiceMade += choices.Add;

        h.Vm.ChooseOutput(ComposerOutput.Original);

        Assert.Equal(ComposerOutput.Original, h.Vm.Output);
        Assert.Equal([new ComposerChoice(ComposerOutput.Original, h.Vm.TargetLanguage)], choices);
    }

    [Fact]
    public async Task AnOutputThatIsNotOffered_CannotBeChosen()
    {
        await using var h = new Harness(options: ComposerOptions.Default with { Cleanup = false });

        Assert.Throws<ArgumentOutOfRangeException>(() => h.Vm.ChooseOutput(ComposerOutput.Clean));
    }

    [Fact]
    public async Task SwappingTheLanguage_IsAChoice()
    {
        await using var h = new Harness();
        var choices = new List<ComposerChoice>();
        h.Vm.ChoiceMade += choices.Add;
        var before = h.Vm.TargetLanguage;

        h.Vm.SwapLanguageCommand.Execute(null);

        Assert.NotEqual(before, h.Vm.TargetLanguage);
        Assert.Equal([new ComposerChoice(h.Vm.Output, h.Vm.TargetLanguage)], choices);
    }

    [Fact]
    public async Task ASwitchTheSystemMakes_IsNotAChoice()
    {
        await using var h = new Harness(output: ComposerOutput.Translate);
        var choices = new List<ComposerChoice>();
        h.Vm.ChoiceMade += choices.Add;
        h.Translator.Handler = (_, ct) => new TaskCompletionSource<TranslatedText>().Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Working);
        h.Time.Advance(ComposerViewModel.SlowAfter + TimeSpan.FromSeconds(1));

        await h.Vm.RunBannerActionCommand.ExecuteAsync(BannerAction.UseOriginal);

        Assert.Equal(ComposerOutput.Original, h.Vm.Output);
        Assert.Empty(choices);
    }

    [Fact]
    public async Task ARememberedStart_IsAnnouncedWhileItStands_AndNotAfterTheUserChangesIt()
    {
        await using var h = new Harness(output: ComposerOutput.Clean);
        h.Vm.Begin(Start(ComposerOutput.Translate, TargetLanguage.English));

        Assert.Equal("Slack", h.Vm.Remembered?.Name);

        h.Vm.ChooseOutput(ComposerOutput.Clean);

        Assert.Null(h.Vm.Remembered);
    }

    [Theory]
    [InlineData(false, "com.slack")]
    [InlineData(true, null)]
    public async Task AStartThatIsNotARememberedChoice_IsNotAnnounced(bool applied, string? appId)
    {
        await using var h = new Harness();

        h.Vm.Begin(Start(applied: applied, appId: appId));

        Assert.Null(h.Vm.Remembered);
    }

    [Fact]
    public async Task RestoringTheDefaults_GoesBack_AndSaysSo()
    {
        await using var h = new Harness(output: ComposerOutput.Clean);
        h.Vm.Begin(Start(ComposerOutput.Translate, TargetLanguage.Chinese));
        var restored = 0;
        h.Vm.DefaultsRestored += () => restored++;

        h.Vm.RestoreDefaultsCommand.Execute(null);

        Assert.Equal((ComposerOutput.Clean, TargetLanguage.English), (h.Vm.Output, h.Vm.TargetLanguage));
        Assert.Equal(1, restored);
        Assert.Null(h.Vm.Remembered);
    }

    [Fact]
    public async Task AnExport_IsAnnounced_AndBecomesTheFormatTheDialogOffersFirst()
    {
        await using var h = new Harness();
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        var formats = new List<ExportFormat>();
        h.Vm.Exported += formats.Add;
        var path = Path.Combine(Path.GetTempPath(), $"livelingo-{Guid.NewGuid():N}.md");

        try
        {
            await h.Vm.ExportAsync(path, ExportFormat.Markdown);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Equal([ExportFormat.Markdown], formats);
        Assert.Equal(ExportFormat.Markdown, h.Vm.LastExportFormat);
    }

    [Fact]
    public async Task AnExportThatFailed_IsNotRemembered()
    {
        await using var h = new Harness();
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        var formats = new List<ExportFormat>();
        h.Vm.Exported += formats.Add;

        await h.Vm.ExportAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "a.md"), ExportFormat.Markdown);

        Assert.Empty(formats);
        Assert.Null(h.Vm.LastExportFormat);
    }

    [Fact]
    public async Task TheComposerRemembersFor_TheContextOfItsStart()
    {
        await using var h = new Harness();

        Assert.Null(h.Vm.RememberFor);

        h.Vm.Begin(Start(applied: false));

        Assert.Equal("Slack", h.Vm.RememberFor?.Name);
    }
}
