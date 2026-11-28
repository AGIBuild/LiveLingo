using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Drafting;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Tests.Cleanup;

public class PolishSchedulerTests
{
    private sealed class BlockingLogger : ILogger<PolishScheduler>
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entered.Set();
            Release.Wait(5000);
        }
    }

    private sealed class FakeCleaner(Func<PolishRequest, string> answer) : IDictationPolisher
    {
        public List<PolishRequest> Requests { get; } = [];
        public List<string> Summaries { get; } = [];

        public Task<string> PolishAsync(PolishRequest request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add(request);
            return Task.FromResult(answer(request));
        }

        public Task<string> SummarizeAsync(string text, CancellationToken ct)
        {
            Summaries.Add(text);
            return Task.FromResult("The whole dictation.");
        }
    }

    private static readonly (string Raw, string Clean)[] Steps =
    [
        ("呃，先改接口。", "先改接口。"),
        ("嗯，然后补测试。", "补测试。"),
        ("那个，最后更新文档。", "最后更新文档。"),
    ];

    private static string Strip(string text) =>
        text.Replace("呃，", "").Replace("嗯，", "").Replace("那个，", "").Replace("然后", "");

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }

    private static DraftSession Dictated(IEnumerable<(string Raw, string Clean)> passages, bool recording = true)
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(draft =>
        {
            draft.Output = OutputKind.Clean;
            draft.Recording = recording;
            foreach (var (raw, clean) in passages)
                draft.Complete(draft.Append(raw), UnitKind.Clean, clean);
        });
        return session;
    }

    private static string[] Raws(DraftSession session) => session.Read(d => d.Units.Select(u => u.Raw).ToArray());

    [Fact]
    public async Task WhenRecordingStops_NeighbouringPassagesArePolishedAsOneText()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, contextChars: 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 1);

        Assert.Equal(["呃，先改接口。嗯，然后补测试。那个，最后更新文档。"], Raws(session));
        Assert.Equal("先改接口。补测试。最后更新文档。", session.Read(d => d.Units[0].Clean.Text));
        Assert.Equal(PolishStyle.Default, cleaner.Requests.Single().Style);
        // Whole dictation already fits in one polish group, so no redundant pre-summary call is issued.
        Assert.Empty(cleaner.Summaries);
    }

    [Fact]
    public async Task LongDictationSpanningMultipleGroups_SummarizesOnceBeforePolishing()
    {
        var partA = new string('甲', 500) + "。";
        var partB = new string('乙', 500) + "。";
        var session = Dictated([(partA, partA), (partB, partB)]);
        var cleaner = new FakeCleaner(request => request.Target);
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, contextChars: 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));

        Assert.Single(cleaner.Summaries);
        Assert.Equal(2, cleaner.Requests.Count);
        Assert.All(cleaner.Requests, r => Assert.Equal("The whole dictation.", r.Summary));
    }

    [Fact]
    public async Task NothingIsPolished_WhileRecording()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
        Assert.Equal(3, Raws(session).Length);
    }

    [Fact]
    public async Task EvenASinglePassage_GetsTheFinalStyle()
    {
        var session = Dictated([Steps[0]]);
        var polisher = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));
        Assert.Single(polisher.Requests);
    }

    [Fact]
    public async Task InvalidCustomOutput_IsRetriedWithTheDefaultStyle()
    {
        const string custom = "Please invent a new deployment date.";
        var session = Dictated(Steps);
        var polisher = new FakeCleaner(request => request.Style == custom ? "Invented date 2040." : Strip(request.Target));
        await using var polish = new PolishScheduler(session, polisher, [], custom, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));
        Assert.Equal([custom, PolishStyle.Default], polisher.Requests.Select(r => r.Style));
        Assert.Single(Raws(session));
        Assert.Equal(PolishOutcome.DefaultRequirements, session.Read(d => d.PolishOutcome));
    }

    [Fact]
    public async Task ResumingRecording_DiscardsTheOldPolish()
    {
        var session = Dictated(Steps);
        var release = new ManualResetEventSlim();
        var polisher = new FakeCleaner(request =>
        {
            release.Wait(5000);
            return Strip(request.Target);
        });
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => polisher.Requests.Count == 1);
        session.Mutate(d => d.Recording = true);
        release.Set();
        await Task.Delay(100);
        Assert.Equal(3, Raws(session).Length);
    }

    [Fact]
    public async Task AnOldFailure_CannotFinishANewerStop()
    {
        var session = Dictated(Steps);
        var logger = new BlockingLogger();
        var calls = 0;
        var polisher = new FakeCleaner(r => Interlocked.Increment(ref calls) == 1
            ? throw new HttpRequestException("offline")
            : Strip(r.Target));
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300, logger);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => logger.Entered.IsSet);
        session.Mutate(d =>
        {
            d.Recording = true;
            d.Complete(d.Append("呃，再发布。"), UnitKind.Clean, "再发布。");
            d.Recording = false;
        });
        logger.Release.Set();
        await WaitUntil(() => !session.Read(d => d.Polishing));
        Assert.Equal(2, polisher.Requests.Count);
        Assert.Single(Raws(session));
    }

    [Fact]
    public async Task AnEditedTranslation_IsARunBoundary_NotAReasonToDropAllPolish()
    {
        var session = Dictated(Steps);
        session.Mutate(d => d.Edit(0, UnitKind.Translation, "My translation."));
        var polisher = new FakeCleaner(r => Strip(r.Target));
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));
        Assert.Equal(2, Raws(session).Length);
        Assert.Equal("My translation.", session.Read(d => d.Units[0].Translation.Text));
        Assert.Equal(Steps[1].Raw + Steps[2].Raw, polisher.Requests.Single().Target);
    }

    [Fact]
    public async Task SwitchingAwayAndBack_DoesNotLoseTheCancelledPolish()
    {
        var session = Dictated(Steps);
        var release = new ManualResetEventSlim();
        var polisher = new FakeCleaner(request =>
        {
            release.Wait(5000);
            return Strip(request.Target);
        });
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => polisher.Requests.Count == 1);
        session.Mutate(d => d.Output = OutputKind.Raw);
        session.Mutate(d => d.Output = OutputKind.Clean);
        release.Set();
        await WaitUntil(() => !session.Read(d => d.Polishing));
        Assert.Equal(2, polisher.Requests.Count);
        Assert.Single(Raws(session));
    }

    [Fact]
    public async Task ADraftThatWasNeverRecorded_IsNotPolished()
    {
        var session = Dictated(Steps, recording: false);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
    }

    [Fact]
    public async Task ARefusedPolish_KeepsThePassages_AndIsNotTriedAgain()
    {
        var session = Dictated([("端口改成 8080。", "端口改成 8080。"), ("超时设成三十秒。", "超时设成三十秒。")]);
        var cleaner = new FakeCleaner(_ => "端口改一下。");
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1);
        await Task.Delay(100);
        session.Mutate(d => d.Output = OutputKind.Clean);
        await Task.Delay(100);

        Assert.Equal(2, Raws(session).Length);
        Assert.Single(cleaner.Requests);
        Assert.Equal(PolishOutcome.KeptPreview, session.Read(d => d.PolishOutcome));
    }

    [Fact]
    public async Task AFailingCleaner_ChangesNothing()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(_ => throw new HttpRequestException("offline"));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1 && !session.Read(d => d.Polishing));

        Assert.Equal(3, Raws(session).Length);
        Assert.Single(cleaner.Requests);
    }

    [Fact]
    public async Task AHandEditedPassage_EndsARun()
    {
        var session = Dictated([.. Steps, ("呃，再发布。", "再发布。")]);
        session.Mutate(d => d.Edit(2, UnitKind.Clean, "我改过的。"));
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 3);

        Assert.Equal(["呃，先改接口。嗯，然后补测试。", "那个，最后更新文档。", "呃，再发布。"], Raws(session));
        Assert.Equal("我改过的。", session.Read(d => d.Units[1].Clean.Text));
    }

    [Fact]
    public async Task LongDictation_IsPolishedInBoundedGroups_AndLaterGroupsReadTheEarlierPolish()
    {
        var body = new string('字', 300);
        var session = Dictated(Enumerable.Range(0, 5).Select(i => ($"呃，{body}{i}。", $"{body}{i}。")));
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 3 && !session.Read(d => d.Polishing));

        Assert.Equal(3, cleaner.Requests.Count);
        Assert.All(cleaner.Requests, request => Assert.True(request.Target.Length <= PolishScheduler.MaxGroupChars));
        Assert.StartsWith($"{body}0。", cleaner.Requests[1].Preceding);
        Assert.Contains("1。", cleaner.Requests[1].Preceding);
        Assert.Contains("The whole dictation.", cleaner.Requests[1].Summary);
    }

    [Fact]
    public async Task RecordingMore_RepolishesTheWholeDraftWithTheNewContext()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 1);

        session.Mutate(d => d.Recording = true);
        session.Mutate(d => d.Complete(d.Append("呃，再发布。"), UnitKind.Clean, "再发布。"));
        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));

        Assert.Equal(2, cleaner.Requests.Count);
        Assert.Single(Raws(session));
    }

    [Fact]
    public async Task WritingWaitsForThePolish_BecauseTheDraftIsStillProcessing()
    {
        var session = Dictated(Steps);
        var release = new ManualResetEventSlim();
        var cleaner = new FakeCleaner(request =>
        {
            release.Wait(5000);
            return Strip(request.Target);
        });
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => cleaner.Requests.Count == 1);

        Assert.True(session.Read(d => d.Polishing));
        Assert.Equal(BlockReason.Processing, session.Read(d => d.InsertionReadiness().Reason));

        release.Set();
        await WaitUntil(() => !session.Read(d => d.Polishing));

        Assert.True(session.Read(d => d.InsertionReadiness().IsReady));
        Assert.Single(Raws(session));
    }

    [Fact]
    public async Task ThePolishStartsFromTheRawText_WithoutWaitingForPassageCleanup()
    {
        var session = Dictated(Steps);
        var last = session.Mutate(d => d.Append("呃，再发布。"));
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => Raws(session).Length == 1);

        Assert.Equal("呃，先改接口。嗯，然后补测试。那个，最后更新文档。呃，再发布。", Raws(session)[0]);
        Assert.Single(cleaner.Requests);
        Assert.False(session.Mutate(d => d.Complete(last, UnitKind.Clean, "Late passage cleanup.")));
    }

    [Fact]
    public async Task ARefusedDefaultRun_KeepsThePreviewWithoutRecursiveModelCalls()
    {
        var session = Dictated(
        [
            ("呃，先改接口。", "先改接口。"), ("嗯，然后补测试。", "补测试。"),
            ("呃，端口改成 8080。", "端口改成 8080。"), ("嗯，再发布。", "再发布。")
        ]);
        var cleaner = new FakeCleaner(request => request.Target.Contains("8080") ? "端口改一下。" : Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));

        Assert.Single(cleaner.Requests);
        Assert.Equal(4, Raws(session).Length);
    }

    [Fact]
    public async Task ATruncatedDefaultAnswer_DoesNotMultiplyModelCalls()
    {
        var session = Dictated([.. Steps, ("嗯，再发布。", "再发布。")]);
        var cleaner = new FakeCleaner(request =>
            request.Target.Contains("先改接口") && request.Target.Contains("再发布")
                ? throw new InvalidDataException("finish=length")
                : Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => !session.Read(d => d.Polishing));

        Assert.Single(cleaner.Requests);
        Assert.Equal(4, Raws(session).Length);
    }

    [Fact]
    public async Task ADraftWithAnotherOutput_IsNotHeldBackByThePolish()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        await using var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);
        session.Mutate(d => d.Output = OutputKind.Raw);

        session.Mutate(d => d.Recording = false);
        await Task.Delay(100);

        Assert.True(session.Read(d => d.Polishing));
        Assert.NotEqual(BlockReason.Processing, session.Read(d => d.ContentReadiness().Reason));
        Assert.Empty(cleaner.Requests);
    }

    [Fact]
    public async Task Dispose_StopsThePolish()
    {
        var session = Dictated(Steps);
        var cleaner = new FakeCleaner(request => Strip(request.Target));
        var polish = new PolishScheduler(session, cleaner, [], PolishStyle.Default, 300);

        await polish.DisposeAsync();
        session.Mutate(d => d.Recording = false);
        await Task.Delay(100);

        Assert.Empty(cleaner.Requests);
    }

    [Fact]
    public async Task NativeAsrPolishedUnits_BypassCascadePolishWithDefaultStyle_UntilCustomStyleOrExplicitRetry()
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = OutputKind.Clean;
            d.Recording = true;
            d.AppendDictated("那个，请把祺元项目的报告发给洛柯。", "请把祺元项目的报告发给洛柯。");
            d.AppendDictated("下周我们要上线 LiveLingo 系统。", "下周我们要上线 LiveLingo 系统。");
        });

        var textCleanerCalls = 0;
        var textCleaner = new CountingTextCleaner(() => Interlocked.Increment(ref textCleanerCalls));
        var cleaner = new FakeCleaner(request => request.Target.Replace("那个，", "").Replace("下周我们要", "下周我们"));
        await using var cleanup = new CleanupScheduler(session, textCleaner, ["LiveLingo"], 300);
        await using var polish = new PolishScheduler(session, cleaner, ["LiveLingo"], PolishStyle.Default, 300);

        session.Mutate(d => d.Recording = false);
        await WaitUntil(() => session.Read(d => d.Final(UnitKind.Clean).State == DocumentState.Ready));

        Assert.Equal(0, textCleanerCalls);
        Assert.Empty(cleaner.Requests);
        Assert.Equal(
            "请把祺元项目的报告发给洛柯。下周我们要上线 LiveLingo 系统。",
            session.Read(d => d.Text()));

        // Explicit user "Regenerate full text" runs the LLM polisher on demand.
        session.Mutate(d => d.RetryDocument(UnitKind.Clean));
        await WaitUntil(() => cleaner.Requests.Count == 1 && session.Read(d => d.Final(UnitKind.Clean).State == DocumentState.Ready));
        Assert.Equal("请把祺元项目的报告发给洛柯。下周我们上线 LiveLingo 系统。", session.Read(d => d.Text()));
    }

    private sealed class CountingTextCleaner(Action onCall) : ITextCleaner
    {
        public Task<string> CleanAsync(CleanupRequest request, CancellationToken ct)
        {
            onCall();
            return Task.FromResult(request.Target);
        }
    }

    private sealed class StreamingPolisher : IDictationPolisher
    {
        public List<(string Style, PolishGateKind Gate)> WarmCalls { get; } = [];
        public Func<PolishRequest, CancellationToken, Task<string>> Handler { get; set; } =
            (req, _) => Task.FromResult(req.Target);

        public Task WarmPresetAsync(
            string style,
            PolishGateKind gate,
            IReadOnlyList<string>? glossary = null,
            CancellationToken ct = default)
        {
            lock (WarmCalls)
                WarmCalls.Add((style, gate));
            return Task.CompletedTask;
        }

        public Task<string> SummarizeAsync(string text, CancellationToken ct) => Task.FromResult("摘要");

        public Task<string> PolishAsync(PolishRequest request, CancellationToken ct) => Handler(request, ct);
    }

    [Fact]
    public async Task PresetScheduler_WarmsSystemPromptOnceWhileRecording_AndStreamsBeforeGateUnlock()
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = OutputKind.Clean;
            d.SetPolishMode(PolishPresets.AiTaskId);
        });
        var finishGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polisher = new StreamingPolisher
        {
            Handler = async (req, ct) =>
            {
                req.OnPartial?.Invoke("### 目标\n排查 `AuthMiddleware`");
                await finishGate.Task.WaitAsync(ct);
                return "### 目标\n排查 `AuthMiddleware` 超时 30 秒。";
            }
        };
        await using var scheduler = new PolishScheduler(session, polisher, ["AuthMiddleware"], PolishPresets.BuiltIns, 300);

        session.Mutate(d =>
        {
            d.Recording = true;
            d.SetPending("正在说");
            d.AppendDictated("帮我排查 AuthMiddleware 超时 30 秒。");
        });
        await WaitUntil(() => polisher.WarmCalls.Count == 1);
        // Additional live ASR updates do not re-trigger system-prompt KV warm-up for the same preset.
        session.Mutate(d => d.SetPending("更多语音"));
        await Task.Delay(30);
        Assert.Single(polisher.WarmCalls);
        Assert.Equal(PolishGateKind.FactAnchor, polisher.WarmCalls[0].Gate);

        // Switching mode mid-recording warms the newly chosen preset's system prompt.
        session.Mutate(d => d.SetPolishMode(PolishPresets.TodoId));
        await WaitUntil(() => polisher.WarmCalls.Count == 2);
        session.Mutate(d => d.SetPolishMode(PolishPresets.AiTaskId));
        await WaitUntil(() => polisher.WarmCalls.Count == 3);

        // Stopping starts streaming immediately while keeping commit blocked until gate verification completes.
        session.Mutate(d =>
        {
            d.SetPending("");
            d.Recording = false;
        });
        await WaitUntil(() => session.Read(d => d.Final(UnitKind.Clean).StreamingText == "### 目标\n排查 `AuthMiddleware`"));
        Assert.Equal(DocumentState.Pending, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Equal(BlockReason.Processing, session.Mutate(d => d.RequestCommit()).Blocked);

        finishGate.SetResult();
        await WaitUntil(() => session.Read(d => d.Final(UnitKind.Clean).State == DocumentState.Ready));
        Assert.Null(session.Read(d => d.Final(UnitKind.Clean).StreamingText));
        Assert.Equal("### 目标\n排查 `AuthMiddleware` 超时 30 秒。", session.Read(d => d.Text()));
        Assert.True(session.Mutate(d => d.RequestCommit()).IsSuccess);
    }

    [Fact]
    public async Task PresetScheduler_HardGateFailureDiscardsUnverifiedStreamingText()
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = OutputKind.Clean;
            d.SetPolishMode(PolishPresets.AiTaskId);
        });
        var polisher = new StreamingPolisher
        {
            Handler = (req, _) =>
            {
                const string hijacked = "好的，当然可以！下面是实现代码：\n```python\nprint('hi')\n```";
                req.OnPartial?.Invoke(hijacked);
                return Task.FromResult(hijacked);
            }
        };
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishPresets.BuiltIns, 300);

        session.Mutate(d =>
        {
            d.Recording = true;
            d.AppendDictated("帮我写一个重试脚本。");
            d.Recording = false;
        });

        await WaitUntil(() => session.Read(d => d.Final(UnitKind.Clean).State == DocumentState.Failed));
        Assert.Null(session.Read(d => d.Final(UnitKind.Clean).StreamingText));
        Assert.Equal("帮我写一个重试脚本。", session.Read(d => d.Text()));
    }

    [Fact]
    public async Task PresetScheduler_TargetedRepairRunsSilentlyWithoutStreamingTwice()
    {
        var session = new DraftSession(hasTarget: true);
        session.Mutate(d =>
        {
            d.DocumentMode = true;
            d.Output = OutputKind.Clean;
            d.SetPolishMode(PolishPresets.AiTaskId);
        });
        var streamSnapshots = new List<string>();
        session.Changed += () =>
        {
            var streaming = session.Read(d => d.Final(UnitKind.Clean).StreamingText);
            if (!string.IsNullOrEmpty(streaming))
                streamSnapshots.Add(streaming);
        };

        var attempt = 0;
        var polisher = new StreamingPolisher
        {
            Handler = (req, _) =>
            {
                if (++attempt == 1)
                {
                    // Pass 1 streams but forgets the number 30, triggering 1x RepairFeedback.
                    req.OnPartial?.Invoke("请关闭 `AuthMiddleware` 的重试并调整超时。");
                    return Task.FromResult("请关闭 `AuthMiddleware` 的重试并调整超时。");
                }

                // Pass 2 must NOT carry an OnPartial callback, so StreamingText never wipes and re-streams from scratch.
                Assert.Null(req.OnPartial);
                return Task.FromResult("请关闭 `AuthMiddleware` 的重试并将超时设为 30 秒。");
            }
        };
        await using var scheduler = new PolishScheduler(session, polisher, ["AuthMiddleware"], PolishPresets.BuiltIns, 300);

        session.Mutate(d =>
        {
            d.Recording = true;
            d.AppendDictated("请关闭 AuthMiddleware 的重试并将超时设为 30 秒。");
            d.Recording = false;
        });

        await WaitUntil(() => session.Read(d => d.Final(UnitKind.Clean).State == DocumentState.Ready));
        Assert.Equal(2, attempt);
        Assert.Equal(["请关闭 `AuthMiddleware` 的重试并调整超时。"], streamSnapshots);
        Assert.Equal("请关闭 `AuthMiddleware` 的重试并将超时设为 30 秒。", session.Read(d => d.Text()));
    }
}
