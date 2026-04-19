using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using NSubstitute;

namespace LiveLingo.Core.Tests.Speech;

public sealed class DefaultSpeechEngineSelectorTests
{
    [Theory]
    [InlineData(SttRoutingMode.AccuracyFirst, "sherpa-cohere-transcribe-14lang-int8")]
    [InlineData(SttRoutingMode.MultilingualFirst, "sherpa-sense-voice-zh-en-ja-ko-yue-int8")]
    public void GetEngine_RoutesToEngineMatchingModeDefault(SttRoutingMode mode, string expectedModelId)
    {
        var options = new CoreOptions { SpeechRoutingMode = mode };
        var cohere = StubEngine(ModelRegistry.SherpaCohereTranscribe14LangInt8.Id);
        var sense = StubEngine(ModelRegistry.SherpaSenseVoiceSmallInt8.Id);

        var selector = new DefaultSpeechEngineSelector(options, [cohere, sense]);

        var engine = selector.GetEngine();

        Assert.Contains(expectedModelId, engine.SupportedModelIds);
    }

    [Fact]
    public void GetActiveModel_HonoursOverrideEvenAcrossModes()
    {
        var options = new CoreOptions
        {
            SpeechRoutingMode = SttRoutingMode.MultilingualFirst,
            ActiveSttModelId = ModelRegistry.SherpaCohereTranscribe14LangInt8.Id,
        };
        var selector = new DefaultSpeechEngineSelector(
            options,
            [StubEngine(ModelRegistry.SherpaCohereTranscribe14LangInt8.Id),
             StubEngine(ModelRegistry.SherpaSenseVoiceSmallInt8.Id)]);

        var active = selector.GetActiveModel();

        Assert.Equal(ModelRegistry.SherpaCohereTranscribe14LangInt8.Id, active.Id);
    }

    [Fact]
    public void GetEngine_FallsBackToStub_WhenNoEngineMatchesActiveModel()
    {
        // No engines registered for the resolved model — selector must hand back the stub
        // rather than throw, so the rest of the pipeline can surface an actionable error.
        var options = new CoreOptions { SpeechRoutingMode = SttRoutingMode.MultilingualFirst };

        var selector = new DefaultSpeechEngineSelector(options, [new StubSpeechToTextEngine()]);

        var engine = selector.GetEngine();

        Assert.IsType<StubSpeechToTextEngine>(engine);
    }

    private static ISpeechToTextEngine StubEngine(string modelId)
    {
        var engine = Substitute.For<ISpeechToTextEngine>();
        engine.SupportedModelIds.Returns(new[] { modelId });
        return engine;
    }
}
