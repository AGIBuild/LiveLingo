using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;

namespace LiveLingo.Core.Tests.Speech;

public sealed class SpeechModelRoutingTests
{
    [Theory]
    [InlineData("AccuracyFirst", SttRoutingMode.AccuracyFirst)]
    [InlineData("accuracyfirst", SttRoutingMode.AccuracyFirst)]
    [InlineData("StreamingFirst", SttRoutingMode.StreamingFirst)]
    [InlineData("MultilingualFirst", SttRoutingMode.MultilingualFirst)]
    public void ParseRoutingMode_AcceptsKnownValues(string input, SttRoutingMode expected)
    {
        Assert.Equal(expected, SpeechModelRouting.ParseRoutingMode(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    public void ParseRoutingMode_FallsBackToAccuracyFirst_ForUnknownInput(string? input)
    {
        Assert.Equal(SttRoutingMode.AccuracyFirst, SpeechModelRouting.ParseRoutingMode(input));
    }

    [Theory]
    [InlineData(SttRoutingMode.AccuracyFirst, "sherpa-cohere-transcribe-14lang-int8")]
    [InlineData(SttRoutingMode.MultilingualFirst, "sherpa-sense-voice-zh-en-ja-ko-yue-int8")]
    // StreamingFirst is reserved for a future streaming Zipformer bundle; until that lands the
    // selector intentionally falls back to Cohere so users always get a usable engine.
    [InlineData(SttRoutingMode.StreamingFirst, "sherpa-cohere-transcribe-14lang-int8")]
    public void Resolve_WithoutOverride_ReturnsModeDefault(SttRoutingMode mode, string expectedId)
    {
        var resolved = SpeechModelRouting.Resolve(mode, overrideModelId: null);

        Assert.Equal(expectedId, resolved.Id);
    }

    [Theory]
    [InlineData(SttRoutingMode.AccuracyFirst, "sherpa-cohere-transcribe-14lang-int8")]
    [InlineData(SttRoutingMode.MultilingualFirst, "sherpa-sense-voice-zh-en-ja-ko-yue-int8")]
    [InlineData(SttRoutingMode.StreamingFirst, "sherpa-cohere-transcribe-14lang-int8")]
    public void ResolveDefaultForMode_ReturnsExpectedModel(SttRoutingMode mode, string expectedId)
    {
        var resolved = SpeechModelRouting.ResolveDefaultForMode(mode);

        Assert.Equal(expectedId, resolved.Id);
    }

    [Fact]
    public void Resolve_WithKnownOverride_PrefersOverride()
    {
        var target = ModelRegistry.SherpaSenseVoiceSmallInt8;

        var resolved = SpeechModelRouting.Resolve(SttRoutingMode.AccuracyFirst, target.Id);

        Assert.Equal(target.Id, resolved.Id);
    }

    [Fact]
    public void Resolve_WithCohereOverride_StillReturnsCohereEvenInMultilingualMode()
    {
        var target = ModelRegistry.SherpaCohereTranscribe14LangInt8;

        var resolved = SpeechModelRouting.Resolve(SttRoutingMode.MultilingualFirst, target.Id);

        Assert.Equal(target.Id, resolved.Id);
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("WHISPER-BASE")]
    public void Resolve_WithUnknownOverride_FallsBackToModeDefault(string overrideId)
    {
        var resolved = SpeechModelRouting.Resolve(SttRoutingMode.AccuracyFirst, overrideId);

        Assert.Equal(ModelRegistry.SherpaCohereTranscribe14LangInt8.Id, resolved.Id);
    }
}
