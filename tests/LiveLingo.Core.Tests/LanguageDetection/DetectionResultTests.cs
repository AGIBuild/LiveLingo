using LiveLingo.Core.LanguageDetection;

namespace LiveLingo.Core.Tests.LanguageDetection;

public class DetectionResultTests
{
    [Fact]
    public void RecordEquality()
    {
        var a = new DetectionResult("en", 0.95f);
        var b = new DetectionResult("en", 0.95f);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordInequality()
    {
        var a = new DetectionResult("en", 0.95f);
        var b = new DetectionResult("zh", 0.85f);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FieldAccess()
    {
        var r = new DetectionResult("ja", 0.77f);
        Assert.Equal("ja", r.Language);
        Assert.Equal(0.77f, r.Confidence);
    }
}
