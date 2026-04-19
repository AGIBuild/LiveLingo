using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Speech;
using LiveLingo.Desktop.ViewModels;
using NSubstitute;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class OverlayVoiceInputTests
{
    private static readonly TargetWindowInfo Target = new(1, 2, "slack", "Slack", 0, 0, 1920, 1080);
    private readonly ITranslationPipeline _pipeline = Substitute.For<ITranslationPipeline>();
    private readonly ITextInjector _injector = Substitute.For<ITextInjector>();
    private readonly ITranslationEngine _engine = new DeterministicTranslationEngine();
    private readonly ILocalizationService _loc = new LocalizationService();
    private readonly ISpeechInputCoordinator _coordinator = Substitute.For<ISpeechInputCoordinator>();

    private OverlayViewModel CreateVm(ISpeechInputCoordinator? coordinator = null)
    {
        var settings = new UserSettings();
        return new OverlayViewModel(
            Target, _pipeline, _injector, _engine, settings,
            localizationService: _loc,
            speechCoordinator: coordinator ?? _coordinator);
    }

    [Fact]
    public void IsVoiceAvailable_TrueWhenCoordinatorProvided()
    {
        var vm = CreateVm();
        Assert.True(vm.IsVoiceAvailable);
    }

    [Fact]
    public void IsVoiceAvailable_FalseWhenNoCoordinator()
    {
        var vm = new OverlayViewModel(
            Target, _pipeline, _injector, _engine,
            localizationService: _loc);
        Assert.False(vm.IsVoiceAvailable);
    }

    [Fact]
    public void VoiceState_DefaultsToIdle()
    {
        var vm = CreateVm();
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);
    }

    [Fact]
    public async Task ToggleVoice_FromIdle_CallsStartRecording()
    {
        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        var vm = CreateVm();
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        await _coordinator.Received(1).StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleVoice_FromIdle_ShowsRecordingStatus()
    {
        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        var vm = CreateVm();
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
    }

    [Fact]
    public async Task ToggleVoice_FromRecording_CallsStopAndTranscribe()
    {
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "transcribed text", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        await _coordinator.Received(1).StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleVoice_SuccessfulTranscription_SetsSourceText()
    {
        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "hello world", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal("hello world", vm.SourceText);
    }

    [Fact]
    public async Task ToggleVoice_TranscriptionFailed_ShowsError_DoesNotSetSourceText()
    {
        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed, "decode error"));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SourceText);
        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
    }

    [Fact]
    public async Task ToggleVoice_PermissionDenied_ShowsPermissionError()
    {
        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(false, null, SpeechInputErrorCode.PermissionDenied));

        var vm = CreateVm();
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
        await _coordinator.DidNotReceive().StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToggleVoice_ModelMissing_ShowsModelError()
    {
        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(false, null, SpeechInputErrorCode.ModelMissing));

        var vm = CreateVm();
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
    }

    [Fact]
    public async Task ToggleVoice_Cancelled_NoStatusText()
    {
        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(false, null, SpeechInputErrorCode.Cancelled));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SourceText);
    }

    [Fact]
    public async Task DownloadSttModel_Success_ShowsReady()
    {
        _coordinator.EnsureSttModelAsync(Arg.Any<IProgress<float>?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        var vm = CreateVm();
        await vm.DownloadSttModelCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
    }

    [Fact]
    public async Task DownloadSttModel_Failure_ShowsError()
    {
        _coordinator.EnsureSttModelAsync(Arg.Any<IProgress<float>?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(false, null, SpeechInputErrorCode.TranscriptionFailed, "network error"));

        var vm = CreateVm();
        await vm.DownloadSttModelCommand.ExecuteAsync(null);

        Assert.Contains("network error", vm.VoiceStatusText);
    }

    [Fact]
    public void StateChanged_UpdatesVoiceState()
    {
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<VoiceInputState>? handler = null;
        coordinator.When(c => c.StateChanged += Arg.Any<Action<VoiceInputState>>())
            .Do(ci => handler = ci.Arg<Action<VoiceInputState>>());

        var vm = CreateVm(coordinator);
        Assert.NotNull(handler);

        handler!.Invoke(VoiceInputState.Recording);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        handler!.Invoke(VoiceInputState.Transcribing);
        Assert.Equal(VoiceInputState.Transcribing, vm.VoiceState);

        handler!.Invoke(VoiceInputState.Idle);
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);
        Assert.Equal(string.Empty, vm.VoiceStatusText);
    }

    [Fact]
    public void CancelCommand_CallsCoordinatorCancel()
    {
        var vm = CreateVm();
        vm.CancelCommand.Execute(null);

        _coordinator.Received(1).CancelCurrent();
    }

    [Fact]
    public async Task VoiceInput_DoesNotAffectTranslationState()
    {
        _pipeline.ProcessStreamingAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<TranslationLifecycleEvent>?>())
            .Returns(_ => SingleDeltaAsync("translated"));

        var vm = CreateVm();
        vm.SourceText = "manual input";

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(vm.TranslatedText));

        vm.VoiceState = VoiceInputState.Error;
        vm.VoiceStatusText = "some voice error";

        Assert.False(string.IsNullOrEmpty(vm.TranslatedText));
        Assert.False(vm.IsTranslating);
    }

    [Fact]
    public async Task ToggleVoice_NoCoordinator_DoesNothing()
    {
        var vm = new OverlayViewModel(
            Target, _pipeline, _injector, _engine,
            localizationService: _loc);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SourceText);
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);
    }

    [Fact]
    public async Task SuccessfulTranscription_TriggersTranslationPipeline()
    {
        _pipeline.ProcessStreamingAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<TranslationLifecycleEvent>?>())
            .Returns(_ => SingleDeltaAsync("translated"));

        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "voice text", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("voice text", vm.SourceText);

        await Task.Delay(1000, TestContext.Current.CancellationToken);
        _pipeline.Received().ProcessStreamingAsync(
            Arg.Is<TranslationRequest>(r => r.SourceText == "voice text"),
            Arg.Any<CancellationToken>(),
            Arg.Any<IProgress<TranslationLifecycleEvent>?>());
    }

    [Fact]
    public async Task ToggleVoice_AppendsToExistingSourceText()
    {
        var vm = CreateVm();
        vm.SourceText = "existing text";
        vm.VoiceState = VoiceInputState.Idle;

        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "new voice", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal("existing text new voice", vm.SourceText);
    }

    [Fact]
    public async Task ToggleVoice_EmptySourceText_NoLeadingSpace()
    {
        var vm = CreateVm();
        vm.SourceText = string.Empty;
        vm.VoiceState = VoiceInputState.Idle;

        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "hello", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal("hello", vm.SourceText);
    }

    [Fact]
    public async Task SegmentCommitted_AppendsToExistingSourceText()
    {
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<string>? committedHandler = null;
        coordinator.When(c => c.SegmentCommitted += Arg.Any<Action<string>>())
            .Do(ci => committedHandler = ci.Arg<Action<string>>());

        var vm = CreateVm(coordinator);
        vm.SourceText = "before";

        coordinator.State.Returns(VoiceInputState.Idle);
        coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        vm.VoiceState = VoiceInputState.Recording;
        Assert.NotNull(committedHandler);
        committedHandler!.Invoke("first segment");
        committedHandler!.Invoke("second segment");

        // Append-only: each committed segment is preserved instead of overwritten,
        // which is the regression this contract guards against.
        Assert.Equal("before first segment second segment", vm.SourceText);
    }

    [Fact]
    public async Task PartialPreview_OnlyShownDuringRecording_AndReplacedByCommit()
    {
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<string>? committedHandler = null;
        Action<string>? previewHandler = null;
        coordinator.When(c => c.SegmentCommitted += Arg.Any<Action<string>>())
            .Do(ci => committedHandler = ci.Arg<Action<string>>());
        coordinator.When(c => c.PartialPreview += Arg.Any<Action<string>>())
            .Do(ci => previewHandler = ci.Arg<Action<string>>());

        var vm = CreateVm(coordinator);
        coordinator.State.Returns(VoiceInputState.Idle);
        coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        vm.VoiceState = VoiceInputState.Recording;

        Assert.NotNull(previewHandler);
        Assert.NotNull(committedHandler);

        previewHandler!.Invoke("hello wor");
        Assert.Equal("hello wor", vm.SourceText);

        previewHandler!.Invoke("hello world");
        Assert.Equal("hello world", vm.SourceText);

        // Committing the segment must REPLACE the preview portion (no duplicate text).
        committedHandler!.Invoke("hello world.");
        Assert.Equal("hello world.", vm.SourceText);
    }

    [Fact]
    public void SegmentCommitted_IgnoredWhenIdle()
    {
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<string>? committedHandler = null;
        coordinator.When(c => c.SegmentCommitted += Arg.Any<Action<string>>())
            .Do(ci => committedHandler = ci.Arg<Action<string>>());

        var vm = CreateVm(coordinator);
        vm.SourceText = "original";

        Assert.NotNull(committedHandler);
        committedHandler!.Invoke("should be ignored");

        Assert.Equal("original", vm.SourceText);
    }

    [Fact]
    public async Task NewRecording_ResetsCommittedTranscriptFromPreviousSession()
    {
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<string>? committedHandler = null;
        coordinator.When(c => c.SegmentCommitted += Arg.Any<Action<string>>())
            .Do(ci => committedHandler = ci.Arg<Action<string>>());

        var vm = CreateVm(coordinator);
        coordinator.State.Returns(VoiceInputState.Idle);
        coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));
        coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "first session", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        vm.VoiceState = VoiceInputState.Recording;
        committedHandler!.Invoke("first session");
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("first session", vm.SourceText);

        // Start a new recording — the previous session's segments must NOT bleed
        // into the new session's append buffer.
        vm.VoiceState = VoiceInputState.Idle;
        coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "second", SpeechInputErrorCode.None));
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        vm.VoiceState = VoiceInputState.Recording;
        committedHandler!.Invoke("second");

        Assert.Equal("first session second", vm.SourceText);
    }

    [Fact]
    public async Task ToggleVoice_ExistingTextEndingWithSpace_NoDoubleSpace()
    {
        var vm = CreateVm();
        vm.SourceText = "ends with space ";
        vm.VoiceState = VoiceInputState.Idle;

        _coordinator.State.Returns(VoiceInputState.Idle);
        _coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "next", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal("ends with space next", vm.SourceText);
    }

    [Fact]
    public async Task SuccessfulTranscription_TranslationFailure_ShowsFriendlyTranslationStatus()
    {
        _pipeline.ProcessStreamingAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<TranslationLifecycleEvent>?>())
            .Returns(_ => ThrowingDeltaAsync(new TranslationFailedException("Translation failed.")));

        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "voice text", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        Assert.Equal("voice text", vm.SourceText);
        Assert.Contains("Translation failed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulTranscription_UnsupportedPair_ShowsUnsupportedPairStatus()
    {
        var settings = new UserSettings
        {
            Translation = new LiveLingo.Desktop.Services.Configuration.TranslationSettings
            {
                DefaultSourceLanguage = "ja",
                DefaultTargetLanguage = "en"
            }
        };

        _pipeline.ProcessStreamingAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<TranslationLifecycleEvent>?>())
            .Returns(_ => ThrowingDeltaAsync(new NotSupportedException("unsupported pair")));

        var vm = new OverlayViewModel(
            Target, _pipeline, _injector, _engine, settings,
            localizationService: _loc,
            speechCoordinator: _coordinator);
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "voice text", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        Assert.Equal("voice text", vm.SourceText);
        Assert.Contains("Unsupported language pair", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSpeechCoordinatorEvents()
    {
        var vm = CreateVm();
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);

        // Baseline: while subscribed the VM reflects coordinator state.
        _coordinator.StateChanged += Raise.Event<Action<VoiceInputState>>(VoiceInputState.Recording);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        vm.Dispose();

        // After Dispose the VM must no longer react to global singleton events.
        // Prior to the fix, the singleton coordinator kept a strong reference to
        // every overlay VM through these handlers, leaking VM instances and
        // causing closed overlays to respond to live voice input.
        _coordinator.StateChanged += Raise.Event<Action<VoiceInputState>>(VoiceInputState.Idle);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        _coordinator.SegmentCommitted += Raise.Event<Action<string>>("post-dispose segment");
        _coordinator.PartialPreview += Raise.Event<Action<string>>("post-dispose preview");
        Assert.DoesNotContain("post-dispose", vm.SourceText);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var vm = CreateVm();
        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public async Task SegmentCommitted_FromBackgroundThread_MarshalsToCapturedSyncContext()
    {
        // Regression: HandleSegmentCommitted / HandlePartialPreview / HandleVoiceStateChanged
        // were invoking observable-property setters synchronously on the STT
        // background thread, which crashed Avalonia subscribers with
        // "calling thread cannot access this object". Every coordinator handler
        // must instead post through the SynchronizationContext captured at ctor.
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        Action<string>? committedHandler = null;
        coordinator.When(c => c.SegmentCommitted += Arg.Any<Action<string>>())
            .Do(ci => committedHandler = ci.Arg<Action<string>>());

        var sync = new TrackingSyncContext();
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        OverlayViewModel vm;
        try { vm = CreateVm(coordinator); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }

        vm.VoiceState = VoiceInputState.Recording;
        Assert.NotNull(committedHandler);

        await Task.Run(() => committedHandler!.Invoke("from background"),
            TestContext.Current.CancellationToken);

        Assert.True(sync.PostCount > 0,
            "Coordinator events from a background thread must be posted to the captured UI SyncContext.");
        sync.DrainPending();
        Assert.Contains("from background", vm.SourceText);
    }

    [Fact]
    public async Task ToggleVoice_ExplicitVoiceLanguage_PreservedAcrossSessions()
    {
        // Regression: the previous code unconditionally assigned
        // SelectedVoiceLanguage = SelectedSourceLanguage on every Start, so an
        // explicit voice-language pick from a previous session was silently
        // wiped — leading to the wrong language hint being passed to STT.
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        var capturedHints = new List<string?>();
        coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedHints.Add(ci.ArgAt<string?>(0));
                return new SpeechInputResult(true, null, SpeechInputErrorCode.None);
            });
        coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, null, SpeechInputErrorCode.None));

        var vm = CreateVm(coordinator);
        vm.SelectedVoiceLanguage = new LanguageInfo("ja", "Japanese");

        coordinator.State.Returns(VoiceInputState.Idle);
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        vm.VoiceState = VoiceInputState.Recording;
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        vm.VoiceState = VoiceInputState.Idle;
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(2, capturedHints.Count);
        Assert.All(capturedHints, code => Assert.Equal("ja", code));
    }

    [Fact]
    public async Task ToggleVoice_NoVoiceLanguagePicked_PassesSourceLanguageHintToStt()
    {
        // Regression: when SelectedVoiceLanguage was null we passed null to
        // sherpa-onnx, which then ran internal language ID and mis-detected short
        // Chinese utterances as English / Japanese. The hint must always fall
        // back to the active source language so the recognizer is biased toward
        // the language the user is actually speaking.
        var coordinator = Substitute.For<ISpeechInputCoordinator>();
        string? capturedHint = null;
        coordinator.StartRecordingAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedHint = ci.ArgAt<string?>(0);
                return new SpeechInputResult(true, null, SpeechInputErrorCode.None);
            });

        var settings = new UserSettings
        {
            Translation = new LiveLingo.Desktop.Services.Configuration.TranslationSettings
            {
                DefaultSourceLanguage = "zh",
                DefaultTargetLanguage = "en"
            }
        };
        var vm = new OverlayViewModel(
            Target, _pipeline, _injector, _engine, settings,
            localizationService: _loc,
            speechCoordinator: coordinator);

        coordinator.State.Returns(VoiceInputState.Idle);
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal("zh", capturedHint);
    }

    private sealed class TrackingSyncContext : SynchronizationContext
    {
        private readonly Queue<Action> _queue = new();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            _queue.Enqueue(() => d(state));
        }

        public void DrainPending()
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Invoke();
        }
    }

    private sealed class DeterministicTranslationEngine : ITranslationEngine
    {
        public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        [
            new("zh", "Chinese"),
            new("en", "English"),
            new("ja", "Japanese"),
        ];

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
            => Task.FromResult($"[{sourceLanguage}\u2192{targetLanguage}] {text}");

        public async IAsyncEnumerable<TranslationDelta> TranslateStreamingAsync(
            string text, string sourceLanguage, string targetLanguage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return new TranslationDelta($"[{sourceLanguage}\u2192{targetLanguage}] {text}");
        }

        public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) => true;
        public void Dispose() { }
    }

    private static async IAsyncEnumerable<TranslationDelta> SingleDeltaAsync(
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new TranslationDelta(text);
    }

    private static async IAsyncEnumerable<TranslationDelta> ThrowingDeltaAsync(
        Exception exception,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.FromException(exception);
        yield break;
    }
}
