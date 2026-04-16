using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public sealed class TranslationPromptBuilderTests
{
    [Fact]
    public void BuildDefault_Contains_ThinkTagRule_And_SourceBlock()
    {
        var messages = TranslationPromptBuilder.BuildDefault("你好世界", "Chinese", "English");

        Assert.Equal(2, messages.Count);
        Assert.Contains("Do not use <think> tags", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("<source>\n你好世界\n</source>", messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Gemma_UsesGemmaOptimized_PromptWithBeginInstruction()
    {
        var messages = TranslationPromptBuilder.Build("hello", "English", "Chinese", LocalModelChatTemplate.Gemma);

        Assert.Equal(2, messages.Count);
        Assert.Contains("Begin your response immediately", messages[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("<think>", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<source lang=\"English\">", messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Qwen_UsesQwenOptimized_WithNoThinkInstruction()
    {
        var messages = TranslationPromptBuilder.Build("你好", "Chinese", "English", LocalModelChatTemplate.Qwen);

        Assert.Equal(2, messages.Count);
        Assert.Contains("Do not think out loud", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Do not use <think> tags", messages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Generic_FallsBackToDefault()
    {
        var generic = TranslationPromptBuilder.Build("test", "English", "Japanese", LocalModelChatTemplate.Generic);
        var @default = TranslationPromptBuilder.BuildDefault("test", "English", "Japanese");

        Assert.Equal(@default[0].Content, generic[0].Content);
        Assert.Equal(@default[1].Content, generic[1].Content);
    }

    [Theory]
    [InlineData(LocalModelChatTemplate.Generic)]
    [InlineData(LocalModelChatTemplate.Gemma)]
    [InlineData(LocalModelChatTemplate.Qwen)]
    public void Build_AllTemplates_HaveSystemAndUserMessages(LocalModelChatTemplate template)
    {
        var messages = TranslationPromptBuilder.Build("text", "Chinese", "English", template);

        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
    }
}
