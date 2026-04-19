using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Speech;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Services;

public class SpeechInputCoordinatorTests
{
    private readonly IAudioCaptureService _audioCapture = Substitute.For<IAudioCaptureService>();
    private readonly ISpeechToTextEngine _sttEngine = Substitute.For<ISpeechToTextEngine>();
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();
    private readonly IVoiceActivityDetector _vadDetector = Substitute.For<IVoiceActivityDetector>();
    private readonly ISpeechEngineSelector _engineSelector = Substitute.For<ISpeechEngineSelector>();
    // Real coordinator wrapping the substitute model manager — keeps the global
    // download dedup behaviour under test instead of a mock-on-mock pyramid.
    private readonly InProcessModelDownloadCoordinator _downloadCoordinator;
    private static CancellationToken TestCt => TestContext.Current.CancellationToken;

    public SpeechInputCoordinatorTests()
    {
        _engineSelector.GetEngine().Returns(_sttEngine);
        _engineSelector.GetActiveModel().Returns(ModelRegistry.SherpaCohereTranscribe14LangInt8);
        _downloadCoordinator = new InProcessModelDownloadCoordinator(_modelManager);
    }

    private SpeechInputCoordinator CreateCoordinator() =>
        new(_audioCapture, _engineSelector, _modelManager, _vadDetector, _downloadCoordinator);

    [Fact]
    public async Task StartRecording_PermissionDenied_ReturnsError()
    {
        _audioCapture.GetPermissionStateAsync(Arg.Any<CancellationToken>())
            .Returns(MicrophonePermissionState.Denied);

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.PermissionDenied, result.ErrorCode);
        Assert.Equal(VoiceInputState.Error, coordinator.State);
    }

    [Fact]
    public async Task StartRecording_Success_SetsRecordingState()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.True(result.Success);
        Assert.Equal(VoiceInputState.Recording, coordinator.State);
        await _audioCapture.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartRecording_AlreadyRecording_ReturnsError()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);

        var result = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.AlreadyRecording, result.ErrorCode);
    }

    [Fact]
    public async Task StopAndTranscribe_NotRecording_ReturnsError()
    {
        var coordinator = CreateCoordinator();
        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.NotRecording, result.ErrorCode);
    }

    [Fact]
    public async Task StopAndTranscribe_Success_ReturnsText()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(audio, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechTranscriptionResult("hello", "en", 0.95f));

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);
        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Text);
        Assert.Equal(VoiceInputState.Idle, coordinator.State);
    }

    [Fact]
    public async Task StopAndTranscribe_TranscriptionFails_ReturnsError()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(audio, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<SpeechTranscriptionResult>(_ => throw new InvalidOperationException("decode error"));

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);
        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.TranscriptionFailed, result.ErrorCode);
        Assert.Equal(VoiceInputState.Error, coordinator.State);
    }

    [Fact]
    public async Task StartRecording_ModelMissing_ReturnsError()
    {
        SetupPermissionGranted();
        _modelManager.ListInstalled().Returns(new List<InstalledModel>());

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.ModelMissing, result.ErrorCode);
    }

    [Fact]
    public void CancelCurrent_ResetsToIdle()
    {
        var coordinator = CreateCoordinator();
        coordinator.CancelCurrent();

        Assert.Equal(VoiceInputState.Idle, coordinator.State);
    }

    [Fact]
    public async Task StateChanged_FiresOnTransitions()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(audio, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechTranscriptionResult("test", "en", 0.9f));

        var coordinator = CreateCoordinator();
        var states = new List<VoiceInputState>();
        coordinator.StateChanged += s => states.Add(s);

        await coordinator.StartRecordingAsync(ct: TestCt);
        await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.Contains(VoiceInputState.Recording, states);
        Assert.Contains(VoiceInputState.Transcribing, states);
        Assert.Contains(VoiceInputState.Idle, states);
    }

    [Fact]
    public async Task StartRecording_PlatformNotSupported_ReturnsError()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();
        _audioCapture.StartAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new PlatformNotSupportedException());

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.PlatformNotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task StartRecording_WhileTranscribing_ReturnsAlreadyRecording()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var tcs = new TaskCompletionSource<SpeechTranscriptionResult>();
        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(audio, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(tcs.Task);

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);

        var stopTask = coordinator.StopAndTranscribeAsync(ct: TestCt);
        Assert.Equal(VoiceInputState.Transcribing, coordinator.State);

        var duringTranscribe = await coordinator.StartRecordingAsync(ct: TestCt);
        Assert.False(duringTranscribe.Success);
        Assert.Equal(SpeechInputErrorCode.AlreadyRecording, duringTranscribe.ErrorCode);

        tcs.SetResult(new SpeechTranscriptionResult("done", "en", 0.9f));
        var result = await stopTask;
        Assert.True(result.Success);
    }

    [Fact]
    public async Task StartRecording_RestrictedPermission_ReturnsPermissionDenied()
    {
        _audioCapture.GetPermissionStateAsync(Arg.Any<CancellationToken>())
            .Returns(MicrophonePermissionState.Restricted);
        SetupSttModelInstalled();

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.PermissionDenied, result.ErrorCode);
    }

    [Fact]
    public async Task StartRecording_AudioCaptureThrowsGenericException_ReturnsError()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();
        _audioCapture.StartAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("device busy"));

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: TestCt);

        Assert.False(result.Success);
        Assert.Contains("device busy", result.ErrorMessage);
        Assert.Equal(VoiceInputState.Error, coordinator.State);
    }

    [Fact]
    public async Task StopAndTranscribe_EmptyText_StillSucceeds()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(audio, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpeechTranscriptionResult("", "en", 0.1f));

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);
        var result = await coordinator.StopAndTranscribeAsync(ct: TestCt);

        Assert.True(result.Success);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task EnsureSttModel_DelegatesToModelManagerForActiveModel()
    {
        var coordinator = CreateCoordinator();
        var result = await coordinator.EnsureSttModelAsync(ct: TestCt);
        Assert.True(result.Success);
        await _modelManager.Received().EnsureModelAsync(
            ModelRegistry.SherpaCohereTranscribe14LangInt8,
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCurrent_DuringTranscription_SetsIdle()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var tcs = new TaskCompletionSource<SpeechTranscriptionResult>();
        var audio = new AudioCaptureResult(new byte[320], 16000, 1, TimeSpan.FromMilliseconds(10));
        _audioCapture.StopAsync(Arg.Any<CancellationToken>()).Returns(audio);
        _sttEngine.TranscribeAsync(Arg.Any<AudioCaptureResult>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var coordinator = CreateCoordinator();
        await coordinator.StartRecordingAsync(ct: TestCt);
        var stopTask = coordinator.StopAndTranscribeAsync(ct: TestCt);

        coordinator.CancelCurrent();
        Assert.Equal(VoiceInputState.Idle, coordinator.State);

        tcs.SetCanceled(TestCt);
        var result = await stopTask;
        Assert.False(result.Success);
    }

    [Fact]
    public async Task StartRecording_CancelledToken_ReturnsCancelled()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();
        _audioCapture.StartAsync(Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var coordinator = CreateCoordinator();
        var result = await coordinator.StartRecordingAsync(ct: cts.Token);

        Assert.False(result.Success);
        Assert.Equal(SpeechInputErrorCode.Cancelled, result.ErrorCode);
        Assert.Equal(VoiceInputState.Idle, coordinator.State);
    }

    [Fact]
    public async Task StateChanged_NotFiredForSameState()
    {
        SetupPermissionGranted();
        SetupSttModelInstalled();

        var coordinator = CreateCoordinator();
        var stateChanges = 0;
        coordinator.StateChanged += _ => stateChanges++;

        coordinator.CancelCurrent();
        coordinator.CancelCurrent();

        Assert.Equal(0, stateChanges);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var coordinator = CreateCoordinator();
        coordinator.Dispose();
        coordinator.Dispose();
    }

    private void SetupPermissionGranted()
    {
        _audioCapture.GetPermissionStateAsync(Arg.Any<CancellationToken>())
            .Returns(MicrophonePermissionState.Granted);
    }

    private void SetupSttModelInstalled()
    {
        var sttModel = ModelRegistry.SherpaCohereTranscribe14LangInt8;
        _modelManager.ListInstalled().Returns(new List<InstalledModel>
        {
            new(sttModel.Id, sttModel.DisplayName, "/models/" + sttModel.Id,
                sttModel.SizeBytes, sttModel.Type, DateTime.UtcNow)
        });
        _modelManager.HasAllExpectedLocalAssets(sttModel).Returns(true);
    }
}
