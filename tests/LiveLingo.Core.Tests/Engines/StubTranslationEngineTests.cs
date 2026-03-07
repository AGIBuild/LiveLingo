using LiveLingo.Core.Engines;

namespace LiveLingo.Core.Tests.Engines;

public class StubTranslationEngineTests
{
    private readonly StubTranslationEngine _engine = new();

    [Fact]
    public async Task TranslateAsync_ReturnsFormattedText()
    {
        var result = await _engine.TranslateAsync("你好", "zh", "en", CancellationToken.None);
        Assert.Equal("[EN] 你好", result);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _engine.TranslateAsync("test", "zh", "en", cts.Token));
    }

    [Theory]
    [InlineData("zh", "en")]
    [InlineData("en", "zh")]
    [InlineData("ja", "en")]
    public void SupportsLanguagePair_AlwaysReturnsTrue(string src, string tgt)
    {
        Assert.True(_engine.SupportsLanguagePair(src, tgt));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("zh", "中文")]
    [InlineData("ja", "日本語")]
    public void SupportedLanguages_ContainsLanguage(string code, string displayName)
    {
        Assert.Contains(_engine.SupportedLanguages, l => l.Code == code && l.DisplayName == displayName);
    }

    [Fact]
    public void SupportedLanguages_HasExpectedCount()
    {
        Assert.Equal(3, _engine.SupportedLanguages.Count);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var engine = new StubTranslationEngine();
        engine.Dispose();
    }
}
