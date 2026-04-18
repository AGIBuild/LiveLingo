using LiveLingo.Core.LanguageDetection;

namespace LiveLingo.Core.Tests.LanguageDetection;

public class LinguaLanguageDetectorTests : IDisposable
{
    private readonly LinguaLanguageDetector _detector = new();

    [Theory]
    [InlineData("Hello world, this is a test sentence in English.", "en")]
    [InlineData("Bonjour, comment allez-vous aujourd'hui ?", "fr")]
    [InlineData("Guten Tag, wie geht es Ihnen heute?", "de")]
    [InlineData("Hola, ¿cómo estás hoy?", "es")]
    [InlineData("Olá, como você está hoje?", "pt")]
    [InlineData("你好，今天天气怎么样？", "zh")]
    [InlineData("こんにちは、今日はいい天気ですね。", "ja")]
    [InlineData("안녕하세요, 오늘 날씨가 어때요?", "ko")]
    [InlineData("Здравствуйте, как ваши дела?", "ru")]
    [InlineData("مرحبا، كيف حالك اليوم؟", "ar")]
    public async Task DetectAsync_CorrectlyIdentifiesMajorLanguages(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Fact]
    public async Task DetectAsync_ReturnsConfidenceBetweenZeroAndOne()
    {
        var result = await _detector.DetectAsync("The quick brown fox jumps over the lazy dog.", CancellationToken.None);
        Assert.InRange(result.Confidence, 0.0f, 1.0f);
    }

    [Fact]
    public async Task DetectAsync_EmptyText_ReturnsDefault()
    {
        var result = await _detector.DetectAsync("   ", CancellationToken.None);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task DetectAsync_DistinguishesSimilarLatinLanguages()
    {
        // "prologue" is valid in both English and French, but full sentences should disambiguate.
        var en = await _detector.DetectAsync(
            "The book begins with a short prologue explaining the setting.",
            CancellationToken.None);
        var fr = await _detector.DetectAsync(
            "Le livre commence par un court prologue qui explique le décor.",
            CancellationToken.None);
        Assert.Equal("en", en.Language);
        Assert.Equal("fr", fr.Language);
    }

    public void Dispose() => _detector.Dispose();
}
