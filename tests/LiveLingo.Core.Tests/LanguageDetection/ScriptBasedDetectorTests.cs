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

    // --- Boundary value tests to kill equality/range mutations ---

    [Theory]
    [InlineData("\u4E00\u4E00\u4E00", "zh")]   // CJK lower bound (一)
    [InlineData("\u9FFF\u9FFF\u9FFF", "zh")]   // CJK upper bound
    [InlineData("\u3400\u3400\u3400", "zh")]   // CJK Ext-A lower bound (㐀)
    [InlineData("\u4DBF\u4DBF\u4DBF", "zh")]   // CJK Ext-A upper bound
    public void DetectByScript_CjkBoundaryChars(string text, string expected)
    {
        Assert.Equal(expected, ScriptBasedDetector.DetectByScript(text));
    }

    [Theory]
    [InlineData("\u3040\u3040\u3040", "ja")]   // Hiragana exact lower bound
    [InlineData("\u3041\u3041\u3041", "ja")]   // Hiragana ぁ (lower bound + 1)
    [InlineData("\u30FF\u30FF\u30FF", "ja")]   // Katakana ヿ (upper bound)
    [InlineData("\u3042", "ja")]               // Single hiragana あ
    public void DetectByScript_JapaneseBoundaryChars(string text, string expected)
    {
        Assert.Equal(expected, ScriptBasedDetector.DetectByScript(text));
    }

    [Theory]
    [InlineData("\uAC00\uAC00\uAC00", "ko")]  // Hangul syllable 가 (lower bound)
    [InlineData("\uD7AF\uD7AF\uD7AF", "ko")]  // Hangul syllable upper bound
    [InlineData("\u1100\u1100\u1100", "ko")]   // Hangul jamo ᄀ (lower bound)
    [InlineData("\u11FF\u11FF\u11FF", "ko")]   // Hangul jamo upper bound
    public void DetectByScript_HangulBoundaryChars(string text, string expected)
    {
        Assert.Equal(expected, ScriptBasedDetector.DetectByScript(text));
    }

    [Theory]
    [InlineData("\u0400\u0400\u0400", "ru")]   // Cyrillic lower bound Ѐ
    [InlineData("\u04FF\u04FF\u04FF", "ru")]   // Cyrillic upper bound ӿ
    public void DetectByScript_CyrillicBoundaryChars(string text, string expected)
    {
        Assert.Equal(expected, ScriptBasedDetector.DetectByScript(text));
    }

    [Theory]
    [InlineData("\u0627\u0627\u0627", "ar")]   // Arabic Alef ا
    [InlineData("\u0600\u0600\u0600", "ar")]   // Arabic lower bound ؀
    [InlineData("\u06FF\u06FF\u06FF", "ar")]   // Arabic upper bound
    public void DetectByScript_ArabicBoundaryChars(string text, string expected)
    {
        Assert.Equal(expected, ScriptBasedDetector.DetectByScript(text));
    }

    [Fact]
    public void DetectByScript_LatinBoundary_AtLimit()
    {
        var atLimit = new string((char)0x024F, 3); // ɏ — Latin Extended-B
        Assert.Equal("en", ScriptBasedDetector.DetectByScript(atLimit));
    }

    // --- Whitespace/punctuation skipping (kills || → && and continue removal) ---

    [Fact]
    public void DetectByScript_SingleScriptCharWithPunctuation_NotDiluted()
    {
        Assert.Equal("ko", ScriptBasedDetector.DetectByScript("가!"));
        Assert.Equal("ru", ScriptBasedDetector.DetectByScript("Б."));
        Assert.Equal("ar", ScriptBasedDetector.DetectByScript("ع,"));
    }

    // --- Ratio threshold tests (kills * → / and > → >= mutations) ---

    [Fact]
    public void DetectByScript_HalfHangulHalfLatin_ReturnsEn()
    {
        // hangul=1, latin=1, total=2 → hangul*2=2 is NOT > 2 → not "ko"
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("가a"));
    }

    [Fact]
    public void DetectByScript_HalfCyrillicHalfLatin_ReturnsEn()
    {
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("Бa"));
    }

    [Fact]
    public void DetectByScript_HalfArabicHalfLatin_ReturnsEn()
    {
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("عa"));
    }

    // --- Latin > comparison mutations ---

    [Fact]
    public void DetectByScript_MajorityCjkMinorityLatin_ReturnsZh()
    {
        // kills latin >= total, latin < total, !(latin > total), latin/2 mutations
        Assert.Equal("zh", ScriptBasedDetector.DetectByScript("一二三四五六abc"));
    }

    [Fact]
    public void DetectByScript_MixedLatinCjkEqualCount_ReturnsFallbackZh()
    {
        // latin=1, cjk=1, total=2 → neither majority → fallback cjk>0 → "zh"
        Assert.Equal("zh", ScriptBasedDetector.DetectByScript("a一"));
    }

    // --- Fallback path tests (kills cjk > 0 → cjk < 0 and conditional false/true) ---

    [Fact]
    public void DetectByScript_CjkWithCyrillicNoMajority_ReturnsFallbackZh()
    {
        // cjk=1, cyrillic=1, total=2 → no majority → cjk>0 → "zh"
        Assert.Equal("zh", ScriptBasedDetector.DetectByScript("一Б"));
    }

    [Fact]
    public void DetectByScript_NoCjkNoMajority_ReturnsFallbackEn()
    {
        // hangul=1, cyrillic=1, total=2 → no majority → cjk=0 → "en"
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("가Б"));
    }

    // --- Latin increment direction (kills latin++ → latin-- mutation) ---

    [Fact]
    public void DetectByScript_PureLatinText_ReturnsEn()
    {
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("abcdef"));
    }

    [Fact]
    public void DetectByScript_LatinMajorityWithCjkMinority_ReturnsEn()
    {
        // latin=5, cjk=1, total=6 → latin*2=10>6 → "en"
        // kills: latin or→and (L38), latin++ removal (L40), latin++→-- (L40), latin*/÷ (L50)
        Assert.Equal("en", ScriptBasedDetector.DetectByScript("ABCDE一"));
    }

    [Fact]
    public void DetectByScript_LatinExtendedBMajorityWithCjk_ReturnsEn()
    {
        // kills value<=0x024F → value<0x024F mutation (L39)
        var text = new string((char)0x024F, 3) + "一";
        Assert.Equal("en", ScriptBasedDetector.DetectByScript(text));
    }
}
