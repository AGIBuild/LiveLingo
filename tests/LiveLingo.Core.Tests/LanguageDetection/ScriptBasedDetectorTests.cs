using LiveLingo.Core.LanguageDetection;

namespace LiveLingo.Core.Tests.LanguageDetection;

public class ScriptBasedDetectorTests
{
    private readonly ScriptBasedDetector _detector = new();

    [Theory]
    [InlineData("你好世界", "zh")]
    [InlineData("中文测试内容", "zh")]
    public async Task DetectAsync_Chinese(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Theory]
    [InlineData("Hello world", "en")]
    [InlineData("This is English text", "en")]
    public async Task DetectAsync_English(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Theory]
    [InlineData("こんにちは世界", "ja")]
    [InlineData("ありがとう", "ja")]
    public async Task DetectAsync_Japanese(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Theory]
    [InlineData("안녕하세요", "ko")]
    public async Task DetectAsync_Korean(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Theory]
    [InlineData("Привет мир", "ru")]
    public async Task DetectAsync_Russian(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Theory]
    [InlineData("مرحبا بالعالم", "ar")]
    public async Task DetectAsync_Arabic(string text, string expected)
    {
        var result = await _detector.DetectAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Language);
    }

    [Fact]
    public async Task DetectAsync_EmptyString_ReturnsEnglish()
    {
        var result = await _detector.DetectAsync("", CancellationToken.None);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task DetectAsync_WhitespaceOnly_ReturnsEnglish()
    {
        var result = await _detector.DetectAsync("   ", CancellationToken.None);
        Assert.Equal("en", result.Language);
    }

    [Fact]
    public async Task DetectAsync_Confidence_Is08()
    {
        var result = await _detector.DetectAsync("Hello", CancellationToken.None);
        Assert.Equal(0.8f, result.Confidence);
    }

    [Fact]
    public async Task DetectAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _detector.DetectAsync("test", cts.Token));
    }

    [Fact]
    public void DetectByScript_MixedChineseEnglish_ReturnsChinese()
    {
        Assert.Equal("zh", ScriptBasedDetector.DetectByScript("你好世界测试一些中文内容"));
    }

    [Fact]
    public void DetectByScript_PunctuationOnly_ReturnsEnglish()
    {
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("!@#$%"));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var d = new ScriptBasedDetector();
        d.Dispose();
    }
}
