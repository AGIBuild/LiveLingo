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
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("translated", "zh", "translated", TimeSpan.FromMilliseconds(10), null));

        var vm = CreateVm();
        vm.SourceText = "manual input";

        await Task.Delay(600, TestContext.Current.CancellationToken);

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
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("translated", "zh", "translated", TimeSpan.FromMilliseconds(10), null));

        var vm = CreateVm();
        vm.VoiceState = VoiceInputState.Recording;

        _coordinator.StopAndTranscribeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechInputResult(true, "voice text", SpeechInputErrorCode.None));

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("voice text", vm.SourceText);

        await Task.Delay(600, TestContext.Current.CancellationToken);
        await _pipeline.Received().ProcessAsync(
            Arg.Is<TranslationRequest>(r => r.SourceText == "voice text"),
            Arg.Any<CancellationToken>());
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

        public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage) => true;
        public void Dispose() { }
    }
}
