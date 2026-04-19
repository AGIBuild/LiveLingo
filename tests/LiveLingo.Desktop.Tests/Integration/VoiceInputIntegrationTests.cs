using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Speech;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Integration;

/// <summary>
/// Integration tests using a REAL SpeechInputCoordinator with controllable fakes.
/// Validates the full orchestration flow: permission → model check → record → transcribe → result.
/// </summary>
public class VoiceInputIntegrationTests
{
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    private static readonly ModelDescriptor SttModel = ModelRegistry.SherpaCohereTranscribe14LangInt8;

    private static ISpeechEngineSelector CreateSelector(ISpeechToTextEngine engine)
    {
        var selector = Substitute.For<ISpeechEngineSelector>();
        selector.GetEngine().Returns(engine);
        selector.GetActiveModel().Returns(SttModel);
        return selector;
    }

    private static SpeechInputCoordinator CreateCoordinator(
        IAudioCaptureService audio,
        ISpeechToTextEngine stt,
        IModelManager modelManager,
        IVoiceActivityDetector? vad = null) =>
        new(
            audio,
            CreateSelector(stt),
            modelManager,
            vad ?? new StubVoiceActivityDetector(),
            new InProcessModelDownloadCoordinator(modelManager));

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FullFlow_Record_StopTranscribe_ReturnsText()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("hello world");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var states = new List<VoiceInputState>();
        coordinator.StateChanged += s => states.Add(s);

        var startResult = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(startResult.Success);
        Assert.Equal(VoiceInputState.Recording, coordinator.State);

        var stopResult = await coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.True(stopResult.Success);
        Assert.Equal("hello world", stopResult.Text);
        Assert.Equal(VoiceInputState.Idle, coordinator.State);

        Assert.Equal(
            [VoiceInputState.Recording, VoiceInputState.Transcribing, VoiceInputState.Idle],
            states);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PermissionDenied_BlocksRecording_RecoversAfterGrant()
    {
        var audio = new FakeAudioCaptureService { PermissionState = MicrophonePermissionState.Denied };
        var stt = new FakeSpeechToTextEngine("text");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);

        var denied = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.False(denied.Success);
        Assert.Equal(SpeechInputErrorCode.PermissionDenied, denied.ErrorCode);
        Assert.Equal(VoiceInputState.Error, coordinator.State);

        audio.PermissionState = MicrophonePermissionState.Granted;
        var granted = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(granted.Success);
        Assert.Equal(VoiceInputState.Recording, coordinator.State);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ModelMissing_ThenDownload_ThenRecordSucceeds()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("transcribed");
        _modelManager.ListInstalled().Returns(new List<InstalledModel>());

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);

        var missing = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.False(missing.Success);
        Assert.Equal(SpeechInputErrorCode.ModelMissing, missing.ErrorCode);

        SetupSttModelInstalled();
        var ensured = await coordinator.EnsureSttModelAsync(ct: TestCt);
        Assert.True(ensured.Success);

        var started = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(started.Success);

        var stopped = await coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.Equal("transcribed", stopped.Text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelDuringRecording_ResetsToIdle()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("ignored");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.Equal(VoiceInputState.Recording, coordinator.State);

        coordinator.CancelCurrent();
        Assert.Equal(VoiceInputState.Idle, coordinator.State);

        var stopAfterCancel = await coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.False(stopAfterCancel.Success);
        Assert.Equal(SpeechInputErrorCode.NotRecording, stopAfterCancel.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelDuringRecording_ThenRecordAgain_Succeeds()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("second try");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        await coordinator.StartRecordingAsync(ct: TestCt);
        coordinator.CancelCurrent();

        var again = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(again.Success);

        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.True(result.Success);
        Assert.Equal("second try", result.Text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TranscriptionFailure_SetsErrorState_AllowsRetry()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("ok") { ShouldFail = true };
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        await coordinator.StartRecordingAsync(ct: TestCt);
        var failed = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.False(failed.Success);
        Assert.Equal(SpeechInputErrorCode.TranscriptionFailed, failed.ErrorCode);
        Assert.Equal(VoiceInputState.Error, coordinator.State);

        stt.ShouldFail = false;
        var retryStart = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(retryStart.Success);

        var retryStop = await coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.True(retryStop.Success);
        Assert.Equal("ok", retryStop.Text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DoubleStart_SecondReturnsAlreadyRecording()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var first = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.True(first.Success);

        var second = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.False(second.Success);
        Assert.Equal(SpeechInputErrorCode.AlreadyRecording, second.ErrorCode);

        Assert.Equal(VoiceInputState.Recording, coordinator.State);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StopWithoutStart_ReturnsNotRecording()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.NotRecording, result.ErrorCode);
        Assert.Equal(VoiceInputState.Idle, coordinator.State);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlatformNotSupported_ReturnsError()
    {
        var audio = new StubAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.PlatformNotSupported, result.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EnsureSttModel_DownloadFails_ReportsError()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        _modelManager.ListInstalled().Returns(new List<InstalledModel>());
        _modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("network error"));

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var result = await coordinator.EnsureSttModelAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Contains("network error", result.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EnsureSttModel_Cancelled_ReturnsCancelled()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        _modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(ci =>
            {
                ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
                throw new OperationCanceledException();
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var result = await coordinator.EnsureSttModelAsync(ct: cts.Token);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EnsureSttModel_ReportsProgress()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        _modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var progress = ci.Arg<IProgress<ModelDownloadProgress>?>();
                progress?.Report(new ModelDownloadProgress(SttModel.Id, 50, 100));
                progress?.Report(new ModelDownloadProgress(SttModel.Id, 100, 100));
                await Task.CompletedTask;
            });

        var reported = new List<float>();
        var progress = new Progress<float>(v => reported.Add(v));

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);
        var result = await coordinator.EnsureSttModelAsync(progress, TestCt);

        Assert.True(result.Success);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MultipleSessions_StateTracksCorrectly()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("session");
        SetupSttModelInstalled();

        using var coordinator = CreateCoordinator(audio, stt, _modelManager);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(VoiceInputState.Idle, coordinator.State);
            await coordinator.StartRecordingAsync(ct: TestCt);
            Assert.Equal(VoiceInputState.Recording, coordinator.State);
            var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);
            Assert.True(result.Success);
            Assert.Equal("session", result.Text);
            Assert.Equal(VoiceInputState.Idle, coordinator.State);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispose_DuringRecording_CleansUp()
    {
        var audio = new FakeAudioCaptureService();
        var stt = new FakeSpeechToTextEngine("text");
        SetupSttModelInstalled();

        var coordinator = CreateCoordinator(audio, stt, _modelManager);
        await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.Equal(VoiceInputState.Recording, coordinator.State);

        coordinator.Dispose();
        Assert.Equal(VoiceInputState.Idle, coordinator.State);
    }

    private void SetupSttModelInstalled()
    {
        _modelManager.ListInstalled().Returns(new List<InstalledModel>
        {
            new(SttModel.Id, SttModel.DisplayName, "/models/" + SttModel.Id,
                SttModel.SizeBytes, SttModel.Type, DateTime.UtcNow)
        });
        _modelManager.HasAllExpectedLocalAssets(SttModel).Returns(true);
    }

    /// <summary>
    /// Controllable fake that simulates audio recording without platform dependencies.
    /// </summary>
    private sealed class FakeAudioCaptureService : IAudioCaptureService
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

    /// <summary>
    /// Controllable fake that returns a predetermined transcription.
    /// </summary>
    private sealed class FakeSpeechToTextEngine(string text) : ISpeechToTextEngine
    {
        public bool ShouldFail { get; set; }

        public IReadOnlyCollection<string> SupportedModelIds { get; } = [SttModel.Id];

        public Task<SpeechTranscriptionResult> TranscribeAsync(AudioCaptureResult audio, string? language = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (ShouldFail)
                throw new InvalidOperationException("Simulated transcription failure");
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
}
