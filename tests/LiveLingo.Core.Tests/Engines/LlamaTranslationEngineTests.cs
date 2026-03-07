using LiveLingo.Core.Engines;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Engines;

public class LlamaTranslationEngineTests
{
    [Theory]
    [InlineData("zh", "en", true)]
    [InlineData("en", "zh", true)]
    [InlineData("ja", "en", true)]
    [InlineData("xx", "yy", false)]
    [InlineData("zh", "xyz", false)]
    public void SupportsLanguagePair_ChecksLanguages(string src, string tgt, bool expected)
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        Assert.Equal(expected, engine.SupportsLanguagePair(src, tgt));

        engine.Dispose();
        host.Dispose();
    }

    [Theory]
    [InlineData("zh", "中文")]
    [InlineData("en", "English")]
    [InlineData("ja", "日本語")]
    [InlineData("ko", "한국어")]
    [InlineData("fr", "Français")]
    [InlineData("de", "Deutsch")]
    [InlineData("es", "Español")]
    [InlineData("ru", "Русский")]
    [InlineData("ar", "العربية")]
    [InlineData("pt", "Português")]
    public void SupportedLanguages_ContainsLanguage(string code, string displayName)
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        Assert.Contains(engine.SupportedLanguages, l => l.Code == code && l.DisplayName == displayName);

        engine.Dispose();
        host.Dispose();
    }

    [Fact]
    public void SupportedLanguages_ContainsAtLeast10()
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        Assert.True(engine.SupportedLanguages.Count >= 10);

        engine.Dispose();
        host.Dispose();
    }

    [Fact]
    public void SupportedLanguages_CodesMatchSupportsLanguagePair()
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        foreach (var lang in engine.SupportedLanguages)
            Assert.True(engine.SupportsLanguagePair(lang.Code, "en"));

        engine.Dispose();
        host.Dispose();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        engine.Dispose();
        host.Dispose();
    }

    [Fact]
    public async Task TranslateAsync_ThrowsOnCancellation()
    {
        var host = new QwenModelHost(
            Options.Create(new CoreOptions()),
            Substitute.For<ILogger<QwenModelHost>>());
        var engine = new LlamaTranslationEngine(host, Substitute.For<ILogger<LlamaTranslationEngine>>());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.TranslateAsync("test", "zh", "en", cts.Token));

        engine.Dispose();
        host.Dispose();
    }

    [Theory]
    [InlineData("zh", "en", "Chinese", "English")]
    [InlineData("en", "ja", "English", "Japanese")]
    [InlineData("ko", "zh", "Korean", "Chinese")]
    [InlineData("fr", "en", "French", "English")]
    [InlineData("de", "en", "German", "English")]
    [InlineData("es", "en", "Spanish", "English")]
    [InlineData("ru", "en", "Russian", "English")]
    [InlineData("ar", "en", "Arabic", "English")]
    [InlineData("pt", "en", "Portuguese", "English")]
    public void BuildPrompt_UsesChatMLFormat(string src, string tgt, string srcName, string tgtName)
    {
        var prompt = LlamaTranslationEngine.BuildPrompt("Hello", src, tgt);

        Assert.StartsWith("<|im_start|>system\n", prompt);
        Assert.Contains(srcName, prompt);
        Assert.Contains(tgtName, prompt);
        Assert.Contains("<|im_end|>", prompt);
        Assert.Contains("<|im_start|>user\nHello<|im_end|>", prompt);
        Assert.EndsWith("<|im_start|>assistant\n", prompt);
    }

    [Fact]
    public void BuildPrompt_NeverNestsInstructionTags()
    {
        var prompt = LlamaTranslationEngine.BuildPrompt("Test text", "zh", "en");

        Assert.DoesNotContain("### Instruction", prompt);
        Assert.DoesNotContain("### Response", prompt);
        Assert.DoesNotContain("[INST]", prompt);

        var imStartCount = prompt.Split("<|im_start|>").Length - 1;
        Assert.Equal(3, imStartCount);

        var imEndCount = prompt.Split("<|im_end|>").Length - 1;
        Assert.Equal(2, imEndCount);
    }

    [Theory]
    [InlineData("unknown_lang")]
    [InlineData("xyz")]
    public void BuildPrompt_FallsBackToCodeForUnknownLanguage(string code)
    {
        var prompt = LlamaTranslationEngine.BuildPrompt("text", code, "en");

        Assert.Contains(code, prompt);
    }

    [Fact]
    public void BuildPrompt_PreservesUserText_WithSpecialCharacters()
    {
        var text = "Hello <world> & \"friends\"";
        var prompt = LlamaTranslationEngine.BuildPrompt(text, "en", "zh");

        Assert.Contains($"<|im_start|>user\n{text}<|im_end|>", prompt);
    }

    [Fact]
    public void StopSequences_DoesNotContainDoubleNewline()
    {
        Assert.DoesNotContain("\n\n", LlamaTranslationEngine.StopSequences);
        Assert.Contains("</s>", LlamaTranslationEngine.StopSequences);
        Assert.Contains("<|im_end|>", LlamaTranslationEngine.StopSequences);
    }

    [Fact]
    public void BuildPrompt_PreservesMultilineText()
    {
        var text = "Hello World\n\nThis is a second paragraph.\n\nAnd a third one.";
        var prompt = LlamaTranslationEngine.BuildPrompt(text, "en", "zh");

        Assert.Contains(text, prompt);
        Assert.Contains("\n\n", prompt);
    }
}
