using LiveLingo.Core.LanguageDetection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.LanguageDetection;

public class HybridLanguageDetectorTests : IDisposable
{
    private readonly ScriptBasedDetector _scriptDetector = new();
    private readonly LinguaLanguageDetector _statisticalDetector = new();

    private HybridLanguageDetector BuildDetector() =>
        new(_scriptDetector, _statisticalDetector, NullLogger<HybridLanguageDetector>.Instance);

    [Theory]
    [InlineData("你好，今天天气怎么样？", "zh")]
    [InlineData("こんにちは、今日はいい天気ですね。", "ja")]
    [InlineData("안녕하세요, 오늘 날씨가 어때요?", "ko")]
    [InlineData("Здравствуйте, как ваши дела?", "ru")]
    [InlineData("مرحبا، كيف حالك اليوم؟", "ar")]
    public async Task DetectAsync_NonLatinHighConfidence_UsesScriptFastPath(string text, string expected)
    {
        var detector = BuildDetector();
        var result = await detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
        // Script-stage confidence on single-script text is ≥ 0.8, ruling out statistical round-trip.
        Assert.True(result.Confidence >= 0.8f);
    }

    [Theory]
    [InlineData("The book begins with a short prologue explaining the setting.", "en")]
    [InlineData("Le livre commence par un court prologue qui explique le décor.", "fr")]
    [InlineData("Das Buch beginnt mit einem kurzen Vorwort, das die Handlung erklärt.", "de")]
    [InlineData("El libro comienza con un breve prólogo que explica el escenario.", "es")]
    [InlineData("O livro começa com um breve prólogo explicando o cenário.", "pt")]
    public async Task DetectAsync_LatinScript_DelegatesToStatistical(string text, string expected)
    {
        var detector = BuildDetector();
        var result = await detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Fact]
    public async Task DetectAsync_EmptyText_ReturnsDefault()
    {
        var detector = BuildDetector();
        var result = await detector.DetectAsync("", CancellationToken.None);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task DetectAsync_HighlyMixedScript_FallsThroughToStatistical()
    {
        // ~50/50 Latin and CJK – script detector will return low confidence and defer.
        var detector = BuildDetector();
        var result = await detector.DetectAsync(
            "Hello 世界, this is a mixed script 测试 sample text.",
            CancellationToken.None);
        // We only assert the pipeline did not crash and returned one of the plausible candidates.
        Assert.Contains(result.Language, new[] { "en", "zh" });
    }

    public void Dispose()
    {
        _scriptDetector.Dispose();
        _statisticalDetector.Dispose();
    }
}
