using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Speech;
using LiveLingo.Desktop.ViewModels;
using NSubstitute;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.Integration;

/// <summary>
/// Integration tests exercising the OverlayViewModel with a REAL SpeechInputCoordinator.
/// Validates the full UI-layer flow: button toggle → coordinator orchestration → SourceText → translation pipeline.
/// </summary>
public class VoiceOverlayIntegrationTests
{
    private static readonly TargetWindowInfo Target = new(1, 2, "slack", "Slack", 0, 0, 1920, 1080);
    private readonly ITranslationPipeline _pipeline = Substitute.For<ITranslationPipeline>();
    private readonly ITextInjector _injector = Substitute.For<ITextInjector>();
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private readonly ILocalizationService _loc = new LocalizationService();
    private readonly ITranslationEngine _engine = new TestEngine();

    private static readonly ModelDescriptor SttModel =
        ModelRegistry.AllModels.First(m => m.Type == ModelType.SpeechToText);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ToggleVoice_RecordStop_SourceTextPopulated_TranslationTriggered()
    {
        var audio = new FakeAudioCapture();
        var stt = new FakeStt("hello from mic");
        SetupSttModelInstalled();
        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("translated", "zh", "translated", TimeSpan.FromMilliseconds(5), null));

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator);

        Assert.True(vm.IsVoiceAvailable);
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);
        Assert.True(audio.IsRecording);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("hello from mic", vm.SourceText);
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);

        await Task.Delay(600, TestContext.Current.CancellationToken);
        await _pipeline.Received().ProcessAsync(
            Arg.Is<TranslationRequest>(r => r.SourceText == "hello from mic"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PermissionDenied_ShowsError_NoRecording()
    {
        var audio = new FakeAudioCapture { PermissionState = MicrophonePermissionState.Denied };
        var stt = new FakeStt("ignored");
        SetupSttModelInstalled();

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.False(audio.IsRecording);
        Assert.Equal(VoiceInputState.Error, vm.VoiceState);
        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));
        Assert.Equal(string.Empty, vm.SourceText);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModelMissing_Download_ThenRecordSucceeds()
    {
        var audio = new FakeAudioCapture();
        var stt = new FakeStt("after download");
        _modelManager.ListInstalled().Returns(new List<InstalledModel>());

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Error, vm.VoiceState);
        Assert.False(string.IsNullOrWhiteSpace(vm.VoiceStatusText));

        SetupSttModelInstalled();
        await vm.DownloadSttModelCommand.ExecuteAsync(null);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("after download", vm.SourceText);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelDuringRecording_ResetsVoiceState_SendsCloseMessage()
    {
        var audio = new FakeAudioCapture();
        var stt = new FakeStt("ignored");
        SetupSttModelInstalled();

        var messenger = new WeakReferenceMessenger();
        AppUiRequestKind? receivedKind = null;
        var recipient = new object();
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, msg) =>
            receivedKind = msg.Value.Kind);

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator, messenger);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        vm.CancelCommand.Execute(null);

        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);
        Assert.Equal(AppUiRequestKind.CloseOverlay, receivedKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TranscriptionError_ShowsError_AllowsRetry()
    {
        var audio = new FakeAudioCapture();
        var stt = new FakeStt("success") { ShouldFail = true };
        SetupSttModelInstalled();

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);

        Assert.Equal(VoiceInputState.Error, vm.VoiceState);
        Assert.Equal(string.Empty, vm.SourceText);

        stt.ShouldFail = false;

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal("success", vm.SourceText);
        Assert.Equal(VoiceInputState.Idle, vm.VoiceState);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VoiceAndTranslation_IndependentStatus()
    {
        var audio = new FakeAudioCapture();
        var stt = new FakeStt("voice text");
        SetupSttModelInstalled();

        _pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("translated", "zh", "translated", TimeSpan.FromMilliseconds(5), null));

        using var coordinator = new SpeechInputCoordinator(audio, stt, _modelManager, new StubVoiceActivityDetector());
        var vm = CreateVm(coordinator);

        vm.SourceText = "manual text";
        await Task.Delay(600, TestContext.Current.CancellationToken);
        Assert.Equal("translated", vm.TranslatedText);

        await vm.ToggleVoiceInputCommand.ExecuteAsync(null);
        Assert.Equal(VoiceInputState.Recording, vm.VoiceState);
        Assert.Equal("translated", vm.TranslatedText);
    }

    private OverlayViewModel CreateVm(ISpeechInputCoordinator coordinator, IMessenger? messenger = null)
    {
        var settings = new UserSettings();
        return new OverlayViewModel(
            Target, _pipeline, _injector, _engine, settings,
            localizationService: _loc,
            speechCoordinator: coordinator,
            messenger: messenger);
    }

    private void SetupSttModelInstalled()
    {
        _modelManager.ListInstalled().Returns(new List<InstalledModel>
        {
            new(SttModel.Id, SttModel.DisplayName, "/models/" + SttModel.Id,
                SttModel.SizeBytes, SttModel.Type, DateTime.UtcNow)
        });
    }

    private sealed class FakeAudioCapture : IAudioCaptureService
    {
        public MicrophonePermissionState PermissionState { get; set; } = MicrophonePermissionState.Granted;
        public bool IsRecording { get; private set; }

        public Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task<AudioCaptureResult> StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IsRecording = false;
            return Task.FromResult(new AudioCaptureResult(
                new byte[32000], 16000, 1, TimeSpan.FromSeconds(1)));
        }

        public Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default)
            => Task.FromResult(PermissionState);

        public AudioCaptureResult? GetCurrentBuffer() =>
            IsRecording ? new AudioCaptureResult(new byte[32000], 16000, 1, TimeSpan.FromSeconds(1)) : null;

        public void Dispose() { }
    }

    private sealed class FakeStt(string text) : ISpeechToTextEngine
    {
        public bool ShouldFail { get; set; }

        public Task<SpeechTranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string? language = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ShouldFail)
                throw new InvalidOperationException("Simulated failure");
            return Task.FromResult(new SpeechTranscriptionResult(text, "en", 0.95f));
        }

        public void Dispose() { }
    }

    private sealed class StubVoiceActivityDetector : IVoiceActivityDetector
    {
        public float ProcessFrame(float[] samples) => 0f;
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class TestEngine : ITranslationEngine
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
