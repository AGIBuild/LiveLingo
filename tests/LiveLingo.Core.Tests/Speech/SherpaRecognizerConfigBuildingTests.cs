using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Speech;

/// <summary>
/// Guards against a subtle bug class: <see cref="SherpaOnnx.OfflineRecognizerConfig"/>
/// (and every nested model config) is a value-type. Any new sherpa engine that overrides
/// <see cref="SherpaOfflineRecognizerEngineBase"/> must accept the config by
/// <see langword="ref"/> and populate the model-specific paths — otherwise the native
/// recognizer aborts at <c>offline-recognizer-impl.cc:Create</c> with
/// "Please provide a model" and the only feedback path is a real-machine crash.
///
/// These tests exercise <see cref="SherpaOfflineRecognizerEngineBase.BuildRecognizerConfig"/>
/// directly so the regression is caught at unit-test speed without instantiating native code.
/// </summary>
public sealed class SherpaRecognizerConfigBuildingTests
{
    [Fact]
    public void CohereTranscribeEngine_BuildsConfigWithEncoderDecoderAndTokens()
    {
        var modelDir = "/fake/cohere-dir";
        var modelManager = Substitute.For<IModelManager>();
        modelManager.GetModelDirectory(Arg.Any<string>()).Returns(modelDir);
        var engine = new SherpaCohereTranscribeEngine(
            modelManager,
            new CoreOptions { InferenceThreads = 2 },
            NullLogger<SherpaCohereTranscribeEngine>.Instance);

        var config = engine.BuildRecognizerConfig(modelDir);

        Assert.Equal(Path.Combine(modelDir, "encoder.int8.onnx"), config.ModelConfig.CohereTranscribe.Encoder);
        Assert.Equal(Path.Combine(modelDir, "decoder.int8.onnx"), config.ModelConfig.CohereTranscribe.Decoder);
        Assert.Equal(Path.Combine(modelDir, "tokens.txt"), config.ModelConfig.Tokens);
        Assert.Equal(1, config.ModelConfig.CohereTranscribe.UsePunct);
        Assert.Equal(1, config.ModelConfig.CohereTranscribe.UseItn);
        Assert.Equal(2, config.ModelConfig.NumThreads);
        Assert.Equal("cpu", config.ModelConfig.Provider);
    }

    [Fact]
    public void SenseVoiceEngine_BuildsConfigWithModelLanguageAndTokens()
    {
        var modelDir = "/fake/sense-voice-dir";
        var modelManager = Substitute.For<IModelManager>();
        modelManager.GetModelDirectory(Arg.Any<string>()).Returns(modelDir);
        var engine = new SherpaSenseVoiceTranscribeEngine(
            modelManager,
            new CoreOptions { InferenceThreads = 4 },
            NullLogger<SherpaSenseVoiceTranscribeEngine>.Instance);

        var config = engine.BuildRecognizerConfig(modelDir);

        Assert.Equal(Path.Combine(modelDir, "model.int8.onnx"), config.ModelConfig.SenseVoice.Model);
        Assert.Equal("auto", config.ModelConfig.SenseVoice.Language);
        Assert.Equal(1, config.ModelConfig.SenseVoice.UseInverseTextNormalization);
        Assert.Equal(Path.Combine(modelDir, "tokens.txt"), config.ModelConfig.Tokens);
        Assert.Equal(4, config.ModelConfig.NumThreads);
        Assert.Equal("cpu", config.ModelConfig.Provider);
    }

    [Fact]
    public void BuildRecognizerConfig_DefaultsThreadsBasedOnProcessorCount_WhenInferenceThreadsZero()
    {
        var modelManager = Substitute.For<IModelManager>();
        var engine = new SherpaCohereTranscribeEngine(
            modelManager,
            new CoreOptions { InferenceThreads = 0 },
            NullLogger<SherpaCohereTranscribeEngine>.Instance);

        var config = engine.BuildRecognizerConfig("/whatever");

        Assert.InRange(config.ModelConfig.NumThreads, 1, 4);
    }
}
