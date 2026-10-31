using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using LiveLingo.Desktop.Composer;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerImportTests
{
    private static readonly IReadOnlyList<TimedSegment> Meeting =
    [
        new(TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(4), "先看缓存。"),
        new(new TimeSpan(1, 2, 3), new TimeSpan(1, 2, 8), "再看队列。")
    ];

    private static Harness Create(ComposerOutput output = ComposerOutput.Original, bool modelsInstalled = true)
    {
        var h = new Harness(modelsInstalled, output: output);
        h.Transcriber.Handler = (_, _, _) => Task.FromResult(Meeting);
        return h;
    }

    [Fact]
    public async Task Import_ShowsTheSegmentsAsAReadOnlyTimeline_AndRunsThemThroughTheSelectedOutput()
    {
        await using var h = Create(ComposerOutput.Clean);

        await h.Vm.ImportAsync("/tmp/meeting.wav");

        Assert.Equal(["/tmp/meeting.wav"], h.Transcriber.Paths);
        Assert.True(h.Vm.HasTimeline);
        Assert.True(h.Vm.IsWide);
        Assert.False(h.Vm.IsEditable);
        Assert.False(h.Vm.IsTranscribing);
        Assert.Equal("先看缓存。\n再看队列。", h.Vm.SourceText);
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Equal(["0:01", "1:02:03"], h.Vm.Lines.Select(l => l.Time).ToArray());
        Assert.Equal(["C:先看缓存。", "C:再看队列。"], h.Vm.Lines.Select(l => l.Text).ToArray());
    }

    [Fact]
    public async Task BuildSrt_UsesTheChosenOutputsTextAtTheOriginalTimes()
    {
        await using var h = Create(ComposerOutput.Clean);
        await h.Vm.ImportAsync("a.wav");
        await Wait.Until(() => h.Vm.CanExport);

        var srt = h.Vm.BuildSrt();

        Assert.Equal(
            "1\n00:00:01,200 --> 00:00:04,000\nC:先看缓存。\n\n2\n01:02:03,000 --> 01:02:08,000\nC:再看队列。\n\n",
            srt);

        h.Vm.Output = ComposerOutput.Original;
        Assert.Contains("先看缓存。", h.Vm.BuildSrt());
        Assert.DoesNotContain("C:", h.Vm.BuildSrt());
    }

    [Fact]
    public async Task ExportSrt_WritesUtf8WithoutABom_AndReportsAnUnwritablePath()
    {
        await using var h = Create(ComposerOutput.Original);
        await h.Vm.ImportAsync("a.wav");
        var path = Path.Combine(Path.GetTempPath(), $"livelingo-{Guid.NewGuid():N}.srt");
        try
        {
            await h.Vm.ExportSrtAsync(path);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.StartsWith("1\n00:00:01,200 --> 00:00:04,000\n先看缓存。", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            File.Delete(path);
        }

        await h.Vm.ExportSrtAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "a.srt"));
        Assert.Equal(BannerKind.FileFailed, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task ExportSrt_DoesNothingWithoutAFinishedTimeline()
    {
        await using var h = Create();
        var path = Path.Combine(Path.GetTempPath(), $"livelingo-{Guid.NewGuid():N}.srt");

        await h.Vm.ExportSrtAsync(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ATimelineCannotBeExported_UntilEverySegmentIsReady()
    {
        await using var h = Create(ComposerOutput.Clean);
        var release = new TaskCompletionSource<string>();
        h.Cleaner.Handler = (_, _) => release.Task;

        await h.Vm.ImportAsync("a.wav");

        Assert.False(h.Vm.CanExport);
        release.SetResult("done");
        await Wait.Until(() => h.Vm.CanExport);
    }

    [Fact]
    public async Task ProgressIsForwardedAsAPercentage()
    {
        await using var h = Create();
        var gate = new TaskCompletionSource();
        h.Transcriber.Handler = async (_, progress, _) =>
        {
            progress!.Report(0.25);
            await gate.Task;
            return Meeting;
        };

        var import = h.Vm.ImportAsync("a.wav");
        await Wait.Until(() => h.Vm.TranscribeProgress > 0);

        Assert.True(h.Vm.IsTranscribing);
        Assert.Equal(ComposerStatus.Transcribing, h.Vm.Status);
        Assert.Equal(25, h.Vm.TranscribeProgress, 3);
        gate.SetResult();
        await import;
        Assert.False(h.Vm.IsTranscribing);
    }

    [Fact]
    public async Task Escape_CancelsTheTranscription_AndLeavesTheComposerEmpty()
    {
        await using var h = Create();
        h.Transcriber.Handler = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Meeting;
        };

        var import = h.Vm.ImportAsync("a.wav");
        await Wait.Until(() => h.Vm.IsTranscribing);
        await h.Vm.CancelCommand.ExecuteAsync(null);
        await import;

        Assert.False(h.Vm.IsTranscribing);
        Assert.False(h.Vm.HasTimeline);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        Assert.DoesNotContain("close", h.Events);
        Assert.True(h.Vm.CanReplaceDraft);
    }

    [Fact]
    public async Task ARecordingWithoutSpeech_ShowsABanner_AndImportsNothing()
    {
        await using var h = Create();
        h.Transcriber.Handler = (_, _, _) => Task.FromResult<IReadOnlyList<TimedSegment>>([]);

        await h.Vm.ImportAsync("silence.wav");

        Assert.Equal(BannerKind.NoSpeechFound, h.Vm.Banner.Kind);
        Assert.False(h.Vm.HasTimeline);
    }

    [Theory]
    [InlineData(typeof(UnsupportedAudioException))]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task AFileThatCannotBeRead_ShowsTheReason(Type exception)
    {
        await using var h = Create();
        h.Transcriber.Handler = (_, _, _) => throw (Exception)Activator.CreateInstance(exception, "cannot read")!;

        await h.Vm.ImportAsync("bad.m4a");

        Assert.Equal(BannerKind.FileFailed, h.Vm.Banner.Kind);
        Assert.Equal("cannot read", h.Vm.Banner.Detail);
        Assert.False(h.Vm.HasTimeline);
        Assert.False(h.Vm.IsTranscribing);
    }

    [Fact]
    public async Task MissingSpeechModels_AskForConsent_ThenImportAfterTheDownload()
    {
        await using var h = Create(modelsInstalled: false);
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(call =>
        {
            h.Models.HasAllExpectedLocalAssets(call.Arg<ModelDescriptor>()).Returns(true);
            return Task.CompletedTask;
        });

        await h.Vm.ImportAsync("a.wav");

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.Empty(h.Transcriber.Paths);

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        Assert.Equal(["a.wav"], h.Transcriber.Paths);
        Assert.True(h.Vm.HasTimeline);
        await h.Downloads.Received(1).StartAsync(ModelCatalog.SpeechVad);
        await h.Downloads.DidNotReceive().StartAsync(ModelCatalog.SpeechLive);
    }

    [Fact]
    public async Task ModelsRemovedAfterTheCheck_AskForConsentAgain()
    {
        await using var h = Create();
        h.Transcriber.Handler = (_, _, _) => throw new ModelNotReadyException(ModelType.SpeechToText, "x", "gone", "download");

        await h.Vm.ImportAsync("a.wav");

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.False(h.Vm.IsTranscribing);
    }

    [Fact]
    public async Task ImportNeedsAnEmptyIdleComposer()
    {
        await using var h = Create();
        h.Vm.SourceText = "typed text";
        Assert.False(h.Vm.CanReplaceDraft);

        await h.Vm.ImportAsync("a.wav");

        Assert.Empty(h.Transcriber.Paths);
    }

    [Fact]
    public async Task AfterAnImport_TheMicrophoneAndASecondImportAreRefused()
    {
        await using var h = Create();
        await h.Vm.ImportAsync("a.wav");

        await h.Vm.StartListeningAsync();
        await h.Vm.ImportAsync("b.wav");

        Assert.Equal(0, h.Voice.Starts);
        Assert.Equal(["a.wav"], h.Transcriber.Paths);
        Assert.False(h.Vm.CanReplaceDraft);
    }
}
