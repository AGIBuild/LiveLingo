using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public class TranslationPromptTests
{
    [Fact]
    public void OnlyTermsPresentInTheSource_AreInjected()
    {
        var prompt = TranslationPrompt.Build(new TranslationRequest(
            "缓存穿透会增加 Redis 的负载。",
            TargetLanguage.English,
            [new("缓存穿透", "cache penetration"), new("数据库索引", "database index")],
            "前文的内容"));

        Assert.Contains("缓存穿透 translates to cache penetration", prompt);
        Assert.DoesNotContain("database index", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("前文的内容", prompt);
        Assert.Contains("only output the translated result", prompt);
    }

    [Fact]
    public void Instructions_PrecedeTheTranslationBoundary_AndNeverBecomePartOfTheSource()
    {
        const string source = "不要重试。";

        var prompt = TranslationPrompt.Build(new TranslationRequest(source, TargetLanguage.English, []));

        const string boundary = "Translate the following text";
        Assert.True(prompt.IndexOf("Preserve meaning", StringComparison.Ordinal) < prompt.IndexOf(boundary, StringComparison.Ordinal));
        Assert.EndsWith(":\n\n" + source, prompt);
    }

    [Fact]
    public void MultiLineSource_IsSentWithoutEscaping()
    {
        var prompt = TranslationPrompt.Build(new TranslationRequest("{\n  \"a\": \"你好\"\n}", TargetLanguage.English, []));

        Assert.EndsWith(":\n\n{\n  \"a\": \"你好\"\n}", prompt);
    }

    [Fact]
    public void Target_SelectsTheLanguageName()
    {
        var prompt = TranslationPrompt.Build(new TranslationRequest("Do not retry.", TargetLanguage.Chinese, []));

        Assert.Contains("into Chinese.", prompt);
        Assert.DoesNotContain("Read-only background", prompt);
        Assert.DoesNotContain("Reference the following translations", prompt);
    }

    [Fact]
    public void UnknownTarget_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TranslationPrompt.Build(new TranslationRequest("x", (TargetLanguage)99, [])));
    }
}
