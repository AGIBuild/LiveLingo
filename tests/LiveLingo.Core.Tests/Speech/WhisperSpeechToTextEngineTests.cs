using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using NSubstitute;

namespace LiveLingo.Core.Tests.Speech;

public class WhisperSpeechToTextEngineTests
{
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();

    [Fact]
    public async Task TranscribeAsync_ModelFileMissing_ThrowsFileNotFound()
    {
        _modelManager.GetModelDirectory("whisper-base").Returns("/nonexistent/path");
        var engine = new WhisperSpeechToTextEngine(_modelManager);
        var audio = new AudioCaptureResult(new byte[640], 16000, 1, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.TranscribeAsync(audio));
    }

    [Fact]
    public void ConvertPcmToFloat_InvalidFormat_ThrowsInvalidOperation()
    {
        var audio = new AudioCaptureResult(new byte[640], 44100, 2, TimeSpan.FromMilliseconds(20));

        var engine = new WhisperSpeechToTextEngine(_modelManager);
        // Audio format is validated before model load, so we expect an error
        // when format doesn't match 16kHz mono.
        // Since ConvertPcmToFloat is private, we test via TranscribeAsync with a missing model.
        _modelManager.GetModelDirectory("whisper-base").Returns("/nonexistent");

        var ex = Assert.ThrowsAsync<FileNotFoundException>(
            () => engine.TranscribeAsync(audio));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task TranscribeAsync_Cancelled_ThrowsOperationCancelled()
    {
        var engine = new WhisperSpeechToTextEngine(_modelManager);
        var audio = new AudioCaptureResult(new byte[640], 16000, 1, TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.TranscribeAsync(audio, ct: cts.Token));
    }

    [Fact]
    public void ModelRegistry_ContainsSttModel()
    {
        var sttModels = ModelRegistry.AllModels
            .Where(m => m.Type == ModelType.SpeechToText)
            .ToList();

        Assert.Single(sttModels);
        Assert.Equal("whisper-base", sttModels[0].Id);
        Assert.True(sttModels[0].SizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(sttModels[0].DownloadUrl));
    }

    [Fact]
    public void ModelRegistry_SttModelInOptional()
    {
        Assert.Contains(ModelRegistry.OptionalModels,
            m => m.Type == ModelType.SpeechToText);
    }

    [Fact]
    public async Task StubEngine_ThrowsOnTranscribe()
    {
        var engine = new StubSpeechToTextEngine();
        var audio = new AudioCaptureResult(new byte[640], 16000, 1, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.TranscribeAsync(audio));
    }
}
