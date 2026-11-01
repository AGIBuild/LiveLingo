using LiveLingo.Core.Drafting;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Tests.Composer;
using LiveLingo.Desktop.Web;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Web;

/// <summary>
/// The composer states of the v3 prototype, produced by driving the real view model. They are the golden fixtures the
/// page tests render, so the page is tested against what C# actually sends.
/// </summary>
internal static class ComposerScenarios
{
    public const string Chat = "先检查缓存。然后更新索引。";

    public static IReadOnlyDictionary<string, Func<Task<ComposerViewState>>> All { get; } = new SortedDictionary<string, Func<Task<ComposerViewState>>>
    {
        ["empty"] = async () => { await using var h = new Harness(output: ComposerOutput.Clean); return State(h); },
        ["remembered"] = async () =>
        {
            await using var h = new Harness(output: ComposerOutput.Clean);
            h.Vm.Begin(new ComposerStart(
                ComposerOutput.Translate, TargetLanguage.English, ComposerOutput.Clean, TargetLanguage.English,
                new RememberContext("com.tinyspeck.slackmacgap", "Slack", Applied: true)));
            return State(h);
        },
        ["resume-draft"] = async () =>
        {
            await using var h = new Harness(output: ComposerOutput.Clean);
            var recent = new RecentDrafts(new FakeClock());
            recent.Close("嗯，这个 PR 先不要合并，等 CI 跑完再说。", written: false);
            return State(h, recent);
        },
        ["clean-ready"] = () => Ready("那个我觉得这个方案可以。", ComposerOutput.Clean),
        ["export-last"] = () => Ready(Chat, ComposerOutput.Clean, then: h =>
        {
            h.Vm.Begin(new ComposerStart(
                ComposerOutput.Clean, h.Vm.TargetLanguage, ComposerOutput.Clean, h.Vm.TargetLanguage, null, ExportFormat.Markdown));
            return Task.CompletedTask;
        }),
        ["translate-ready"] = () => Ready(Chat, ComposerOutput.Translate),
        ["original-ready"] = () => Ready(Chat, ComposerOutput.Original),
        ["copy-only"] = () => Ready(Chat, ComposerOutput.Translate, hasTarget: false),
        ["cloud-ctrl-enter"] = () => Ready(Chat, ComposerOutput.Translate, options: new ComposerOptions(true, true, CommitKey.CtrlEnter, true, "Auto", true)),
        ["reduced-motion"] = () => Ready(Chat, ComposerOutput.Translate, options: new ComposerOptions(false, false, CommitKey.Enter, false, "Auto", true)),
        ["no-cleanup"] = () => Ready(Chat, ComposerOutput.Original, options: ComposerOptions.Default with { Cleanup = false }),
        ["translate-pending"] = () => Pending(commit: false),
        ["translate-armed"] = () => Pending(commit: true),
        ["written"] = () => Ready(Chat, ComposerOutput.Translate, then: h => h.Vm.CommitAsync(fromKey: true)),
        ["editing"] = () => Ready(Chat, ComposerOutput.Clean, then: h => { h.Vm.StartEditCommand.Execute(0); return Task.CompletedTask; }),
        ["gate-warning"] = () => Ready(Chat, ComposerOutput.Clean, then: h =>
        {
            h.Vm.Lines = [new ResultLine(0, "先检查缓存。", LineState.Ready, "", Rejection: new GateRejection(GateReason.NumberLost, "8080"), IsOwned: false), h.Vm.Lines[1]];
            return Task.CompletedTask;
        }),
        ["row-failed"] = RowFailed,
        ["whole-wide"] = WholeWide,
        ["listening"] = Listening,
        ["target-lost"] = () => Ready(Chat, ComposerOutput.Translate, then: async h =>
        {
            h.Tracker.IsAlive(Arg.Any<LiveLingo.Desktop.Platform.TargetWindowInfo>()).Returns(false);
            await h.Vm.CommitAsync(fromKey: false);
        }),
        ["write-failed"] = () => Ready(Chat, ComposerOutput.Translate, then: async h =>
        {
            h.Injector.InjectAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("input is locked")));
            await h.Vm.CommitAsync(fromKey: false);
        }),
        ["mic-denied"] = async () =>
        {
            await using var h = new Harness();
            h.Permission.Status.Returns(LiveLingo.Desktop.Platform.MicrophoneAccess.Denied);
            await h.Vm.StartListeningAsync();
            return State(h);
        },
        ["no-microphone"] = async () =>
        {
            await using var h = new Harness();
            h.Voice.StartFailure = new InvalidOperationException("no input device");
            await h.Vm.StartListeningAsync();
            return State(h);
        },
        ["model-missing"] = async () =>
        {
            await using var h = new Harness(modelsInstalled: false);
            h.Vm.SourceText = Chat;
            await h.Vm.CommitAsync(fromKey: false);
            return State(h);
        },
        ["downloading"] = () => WithBanner(new BannerState(BannerKind.Downloading, "", 46)),
        ["download-failed"] = () => WithBanner(new BannerState(BannerKind.DownloadFailed, "network error")),
        ["slow"] = Slow,
        ["timeline"] = () => Imported(Segments("今天的周会", "下周发布", "风险有两个")),
        ["no-speech"] = () => Imported([]),
    };

    public static ComposerViewState State(Harness h, RecentDrafts? recent = null) =>
        Normalize(ComposerViewState.From(h.Vm, Texts.Chinese(), recent ?? new RecentDrafts(), sourceRev: 1));

    /// <summary>Key caps differ per operating system; the fixtures are the macOS ones everywhere.</summary>
    private static ComposerViewState Normalize(ComposerViewState state) => state with
    {
        Rev = 1,
        Options = state.Options with
        {
            CommitKeyCap = state.Options.CommitKey == "enter" ? "↵" : "⌃↵",
            NewlineKeyCap = "⇧↵"
        }
    };

    private static async Task<ComposerViewState> Ready(
        string text, ComposerOutput output, bool hasTarget = true, ComposerOptions? options = null, Func<Harness, Task>? then = null)
    {
        await using var h = new Harness(hasTarget: hasTarget, output: output, options: options);
        h.Vm.SourceText = text;
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        if (then is not null)
            await then(h);
        return State(h);
    }

    private static async Task<ComposerViewState> Pending(bool commit)
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = Chat;
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Working);
        if (commit)
            await h.Vm.CommitAsync(fromKey: true);
        return State(h);
    }

    private static async Task<ComposerViewState> RowFailed()
    {
        await using var h = new Harness();
        h.Translator.Handler = (request, _) => request.Source.Contains("索引")
            ? throw new InvalidOperationException("translation failed")
            : Task.FromResult(new TranslatedText("Check the cache.", 1, 2));
        h.Vm.SourceText = Chat;
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Failed && h.Vm.Lines is [{ State: LineState.Ready }, { State: LineState.Failed }]);
        return State(h);
    }

    private static async Task<ComposerViewState> WholeWide()
    {
        await using var h = new Harness();
        h.Vm.ForceWhole = true;
        h.Vm.SourceText = "# Release notes\n\n- 修复缓存\n- 更新索引\n";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        return State(h);
    }

    private static async Task<ComposerViewState> Listening()
    {
        await using var h = new Harness();
        await h.Vm.StartListeningAsync();
        h.Voice.Say("先检查缓存。");
        await Wait.Until(() => h.Vm.Lines is [{ State: LineState.Ready }]);
        h.Voice.Hear("然后更新");
        return State(h);
    }

    private static async Task<ComposerViewState> WithBanner(BannerState banner)
    {
        await using var h = new Harness();
        h.Vm.Banner = banner;
        return State(h);
    }

    private static async Task<ComposerViewState> Slow()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = Chat;
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Working);
        h.Time.Advance(ComposerViewModel.SlowAfter + TimeSpan.FromSeconds(1));
        return State(h);
    }

    private static IReadOnlyList<TimedSegment> Segments(params string[] texts) =>
        [.. texts.Select((text, i) => new TimedSegment(TimeSpan.FromSeconds(i * 65), TimeSpan.FromSeconds(i * 65 + 5), text))];

    private static async Task<ComposerViewState> Imported(IReadOnlyList<TimedSegment> segments)
    {
        await using var h = new Harness();
        h.Transcriber.Handler = (_, _, _) => Task.FromResult(segments);
        await h.Vm.ImportAsync("/tmp/weekly.wav");
        if (segments.Count > 0)
            await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        return State(h);
    }
}
