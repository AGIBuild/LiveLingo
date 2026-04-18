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

    // --- Explicit rule coverage (each instruction must survive verbatim) ---

    [Fact]
    public void BuildDefault_SystemMessage_ContainsAllTranslationRulesInOrder()
    {
        var messages = TranslationPromptBuilder.BuildDefault("hola", "Spanish", "English");
        var sys = messages[0].Content;

        Assert.Contains("expert translation engine", sys, StringComparison.Ordinal);
        Assert.Contains("from Spanish to English", sys, StringComparison.Ordinal);
        Assert.Contains("Rules:", sys, StringComparison.Ordinal);
        Assert.Contains("Output ONLY the final English translation.", sys, StringComparison.Ordinal);
        Assert.Contains("Do NOT output any Spanish text.", sys, StringComparison.Ordinal);
        Assert.Contains("Do NOT output any explanations, conversational text, or notes.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not use <think> tags", sys, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDefault_UserMessage_ContainsSourceTextInsideSourceTag()
    {
        var messages = TranslationPromptBuilder.BuildDefault("hola", "Spanish", "English");
        var user = messages[1].Content;

        Assert.Contains("Translate the following Spanish text to English", user, StringComparison.Ordinal);
        Assert.Contains("<source>\nhola\n</source>", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Gemma_SystemMessage_KeepsFullInstructionChain()
    {
        var messages = TranslationPromptBuilder.Build("hi", "English", "Chinese", LocalModelChatTemplate.Gemma);
        var sys = messages[0].Content;

        Assert.Contains("professional translator from English to Chinese.", sys, StringComparison.Ordinal);
        Assert.Contains("Respond with the Chinese translation only.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not include any explanation, commentary, or the original text.", sys, StringComparison.Ordinal);
        Assert.Contains("Begin your response immediately with the first word of the translation.", sys, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Gemma_UserMessage_WrapsSourceWithLangAttribute()
    {
        var messages = TranslationPromptBuilder.Build("hi", "English", "Chinese", LocalModelChatTemplate.Gemma);
        var user = messages[1].Content;

        Assert.Contains("<source lang=\"English\">\nhi\n</source>", user, StringComparison.Ordinal);
        Assert.Contains("Translate to Chinese:", user, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Qwen_SystemMessage_ForbidsThinkingAndNotes()
    {
        var messages = TranslationPromptBuilder.Build("你好", "Chinese", "English", LocalModelChatTemplate.Qwen);
        var sys = messages[0].Content;

        Assert.Contains("expert translation engine", sys, StringComparison.Ordinal);
        Assert.Contains("Translate from Chinese to English.", sys, StringComparison.Ordinal);
        Assert.Contains("Output ONLY the translated text.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not think out loud.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not use <think> tags.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not explain.", sys, StringComparison.Ordinal);
        Assert.Contains("Do not add notes.", sys, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Qwen_UserMessage_IsTerseInstruction()
    {
        var messages = TranslationPromptBuilder.Build("你好", "Chinese", "English", LocalModelChatTemplate.Qwen);
        var user = messages[1].Content;

        Assert.Equal("Translate to English:\n你好", user);
    }

    // --- Glossary section ---

    [Fact]
    public void Build_WithEmptyGlossary_OmitsGlossarySection()
    {
        // Both null and empty hints collections must produce the same
        // system message, AND that message must end with the template's
        // final instruction (no stray text appended by the glossary helper).
        var withNone = TranslationPromptBuilder.BuildDefault("x", "en", "fr");
        var withEmpty = TranslationPromptBuilder.BuildDefault("x", "en", "fr", Array.Empty<GlossaryEntry>());

        Assert.DoesNotContain("Glossary", withNone[0].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Glossary", withEmpty[0].Content, StringComparison.Ordinal);
        Assert.Equal(withNone[0].Content, withEmpty[0].Content);
        Assert.EndsWith(
            "Do not use <think> tags or output any thought process.",
            withEmpty[0].Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithGlossary_AppendsEntriesWithArrowDelimiter()
    {
        var hints = new[]
        {
            new GlossaryEntry("API", "接口", "en", "zh"),
            new GlossaryEntry("SDK", "开发包", "en", "zh")
        };

        var messages = TranslationPromptBuilder.BuildDefault("call API", "English", "Chinese", hints);
        var sys = messages[0].Content;

        Assert.Contains("[Glossary - translate these terms exactly as specified]:", sys, StringComparison.Ordinal);
        Assert.Contains("\n- API → 接口", sys, StringComparison.Ordinal);
        Assert.Contains("\n- SDK → 开发包", sys, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Gemma_WithGlossary_AppendsGlossarySectionToSystemMessage()
    {
        var hints = new[] { new GlossaryEntry("foo", "bar", "en", "zh") };

        var messages = TranslationPromptBuilder.Build(
            "foo", "English", "Chinese", LocalModelChatTemplate.Gemma, hints);

        Assert.Contains("Glossary", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("foo → bar", messages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Qwen_WithGlossary_AppendsGlossarySectionToSystemMessage()
    {
        var hints = new[] { new GlossaryEntry("foo", "bar", "en", "zh") };

        var messages = TranslationPromptBuilder.Build(
            "foo", "English", "Chinese", LocalModelChatTemplate.Qwen, hints);

        Assert.Contains("Glossary", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("foo → bar", messages[0].Content, StringComparison.Ordinal);
    }
}
