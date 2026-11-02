using LiveLingo.Core;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

/// <summary>Returns scripted segments (or fails) for an imported recording.</summary>
internal sealed class ScriptedTranscriber : IAudioFileTranscriber
{
    public Func<string, IProgress<double>?, CancellationToken, Task<IReadOnlyList<TimedSegment>>> Handler { get; set; } =
        (_, _, _) => Task.FromResult<IReadOnlyList<TimedSegment>>([]);

    public List<string> Paths { get; } = [];

    public Task<IReadOnlyList<TimedSegment>> TranscribeAsync(
        string path, IReadOnlyList<string> glossary, IProgress<double>? progress, CancellationToken ct)
    {
        Paths.Add(path);
        return Handler(path, progress, ct);
    }
}

/// <summary>A clock a test moves by hand; timers created from it fire only when the test advances time.</summary>
internal sealed class FakeClock : TimeProvider
{
    private sealed class FakeTimer(FakeClock clock, TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
    {
        public DateTimeOffset Next { get; private set; } = due == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock._now + due;
        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
        {
            due = dueTime;
            period = newPeriod;
            Next = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock._now + dueTime;
            return true;
        }

        public void Fire()
        {
            Next = period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : Next + period;
            callback(state);
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly List<FakeTimer> _timers = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            _timers.RemoveAll(t => t.Disposed);
            var next = _timers.OrderBy(t => t.Next).FirstOrDefault();
            if (next is null || next.Next > target)
                break;

            _now = next.Next;
            next.Fire();
        }

        _now = target;
    }
}

internal sealed class ScriptedTranslator : ITranslator
{
    public Func<TranslationRequest, CancellationToken, Task<TranslatedText>> Handler { get; set; } =
        (request, _) => Task.FromResult(new TranslatedText("T:" + request.Source, 1, 2));

    public List<TranslationRequest> Requests { get; } = [];

    public Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        lock (Requests)
            Requests.Add(request);
        return Handler(request, ct);
    }
}

internal sealed class ScriptedCleaner : ITextCleaner
{
    public Func<CleanupRequest, CancellationToken, Task<string>> Handler { get; set; } =
        (request, _) => Task.FromResult("C:" + request.Target);

    public List<CleanupRequest> Requests { get; } = [];

    public Task<string> CleanAsync(CleanupRequest request, CancellationToken ct)
    {
        lock (Requests)
            Requests.Add(request);
        return Handler(request, ct);
    }
}

/// <summary>A voice input that lets a test "speak" by appending recognized sentences to the draft.</summary>
internal sealed class FakeVoiceInput : IVoiceInput
{
    private DraftSession? _draft;

    public event Action<bool>? NoSignalChanged;

    public event Action<bool>? PreparingChanged;

    public bool IsPreparing { get; private set; }

    public void SetPreparing(bool preparing)
    {
        IsPreparing = preparing;
        PreparingChanged?.Invoke(preparing);
    }

    public void RaiseNoSignal(bool silent) => NoSignalChanged?.Invoke(silent);

    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public Exception? StartFailure { get; set; }
    public TaskCompletionSource? StartGate { get; set; }

    public async Task StartAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct)
    {
        Starts++;
        if (StartGate is not null)
            await StartGate.Task;
        if (StartFailure is not null)
            throw StartFailure;

        _draft = draft;
        draft.Mutate(d => d.Recording = true);
    }

    public Task StopAsync()
    {
        Stops++;
        _draft?.Mutate(d =>
        {
            d.SetPending("");
            d.Recording = false;
        });
        _draft = null;
        return Task.CompletedTask;
    }

    public void Say(params string[] sentences) =>
        _draft!.Mutate(d =>
        {
            foreach (var sentence in sentences)
                d.Append(sentence);
        });

    public void Hear(string pending) => _draft!.Mutate(d => d.SetPending(pending));

    public ValueTask DisposeAsync() => new(StopAsync());
}

internal sealed class Harness : IAsyncDisposable
{
    public static readonly TargetWindowInfo Target = new(1, 0, "Slack", "chat", 0, 0, 800, 600);

    public DraftSession Session { get; }
    public ScriptedTranslator Translator { get; } = new();
    public ScriptedCleaner Cleaner { get; } = new();
    public FakeVoiceInput Voice { get; } = new();
    public ScriptedTranscriber Transcriber { get; } = new();
    public TranslationScheduler Scheduler { get; }
    public CleanupScheduler Cleanup { get; }
    public IModelManager Models { get; } = Substitute.For<IModelManager>();
    public IModelDownloadCoordinator Downloads { get; } = Substitute.For<IModelDownloadCoordinator>();
    public IPlatformServices Platform { get; } = Substitute.For<IPlatformServices>();
    public ITextInjector Injector { get; } = Substitute.For<ITextInjector>();
    public IClipboardService Clipboard { get; } = Substitute.For<IClipboardService>();
    public IWindowTracker Tracker { get; } = Substitute.For<IWindowTracker>();
    public IMicrophonePermission Permission { get; } = Substitute.For<IMicrophonePermission>();
    public ModelDescriptor Runtime { get; } = ModelCatalog.LlamaRuntimeFor("osx-arm64");
    public ModelDescriptor TranslatorModel { get; } = ModelCatalog.TranslatorFast;
    public ModelDescriptor CleanerModel { get; } = ModelCatalog.Cleaner;
    public FakeClock Time { get; } = new();
    public ComposerViewModel Vm { get; }
    public List<string> Events { get; } = [];

    /// <param name="output">Most tests exercise translation; the view model itself defaults to cleanup.</param>
    public Harness(
        bool modelsInstalled = true,
        bool hasTarget = true,
        SynchronizationContext? notifyOn = null,
        ComposerOutput? output = ComposerOutput.Translate,
        ComposerOptions? options = null)
    {
        Session = new DraftSession(hasTarget, notifyOn);
        Scheduler = new TranslationScheduler(Session, Translator, TargetLanguage.English, TimeSpan.FromHours(1));
        Cleanup = new CleanupScheduler(Session, Cleaner, [], TimeSpan.FromHours(1), contextUnits: 2);
        Models.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(modelsInstalled);
        Platform.TextInjector.Returns(Injector);
        Platform.Clipboard.Returns(Clipboard);
        Platform.WindowTracker.Returns(Tracker);
        Platform.MicrophonePermission.Returns(Permission);
        Permission.Status.Returns(MicrophoneAccess.Granted);
        Tracker.IsAlive(Arg.Any<TargetWindowInfo>()).Returns(true);
        var plan = new ComposerModelPlan(
            [Runtime, CleanerModel], [Runtime, TranslatorModel], ModelCatalog.Speech, ModelCatalog.Transcription);
        Vm = new ComposerViewModel(
            Session, Scheduler, plan, Models, Downloads, Platform, Voice, Transcriber, [],
            options ?? ComposerOptions.Default, hasTarget ? Target : null, notifyOn, Time);
        if (output is { } start)
            Vm.Output = start;
        Vm.Completed += () => Events.Add("completed");
        Vm.CloseRequested += () => Events.Add("close");
    }

    public async ValueTask DisposeAsync()
    {
        await Vm.DisposeAsync();
        await Cleanup.DisposeAsync();
        await Scheduler.DisposeAsync();
    }
}

internal static class Wait
{
    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(5);
        }
    }
}
