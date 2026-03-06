using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class TranslationRecordTests
{
    [Fact]
    public void TranslationRequest_RecordEquality()
    {
        var a = new TranslationRequest("hello", "en", "zh", null);
        var b = new TranslationRequest("hello", "en", "zh", null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TranslationRequest_WithPostProcessing()
    {
        var opts = new ProcessingOptions(Summarize: true);
        var req = new TranslationRequest("text", "en", "zh", opts);
        Assert.True(req.PostProcessing!.Summarize);
        Assert.False(req.PostProcessing.Optimize);
        Assert.False(req.PostProcessing.Colloquialize);
    }

    [Fact]
    public void TranslationResult_RecordEquality()
    {
        var dur = TimeSpan.FromMilliseconds(100);
        var a = new TranslationResult("hello", "en", "hello", dur, null);
        var b = new TranslationResult("hello", "en", "hello", dur, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TranslationResult_FieldAccess()
    {
        var result = new TranslationResult(
            "final", "zh", "raw", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));

        Assert.Equal("final", result.Text);
        Assert.Equal("zh", result.DetectedSourceLanguage);
        Assert.Equal("raw", result.RawTranslation);
        Assert.Equal(50, result.TranslationDuration.TotalMilliseconds);
        Assert.Equal(10, result.PostProcessingDuration!.Value.TotalMilliseconds);
    }
}
