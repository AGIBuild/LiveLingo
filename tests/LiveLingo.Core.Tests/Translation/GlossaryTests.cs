using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Tests.Translation;

public sealed class InMemoryTranslationGlossaryTests
{
    private static CoreOptions OptionsWithGlossary(params GlossaryEntry[] entries)
    {
        var opts = new CoreOptions();
        opts.Glossary = entries;
        return opts;
    }

    [Fact]
    public void GetRelevantEntries_ReturnsEmpty_WhenGlossaryIsEmpty()
    {
        var sut = new InMemoryTranslationGlossary(new CoreOptions());
        var results = sut.GetRelevantEntries("Hello world", "en", "zh");
        Assert.Empty(results);
    }

    [Fact]
    public void GetRelevantEntries_MatchesCaseInsensitively()
    {
        var opts = OptionsWithGlossary(new GlossaryEntry("AI", "人工智能"));
        var sut = new InMemoryTranslationGlossary(opts);

        var results = sut.GetRelevantEntries("The ai revolution is here.", "en", "zh");

        Assert.Single(results);
        Assert.Equal("AI", results[0].SourceTerm);
    }

    [Fact]
    public void GetRelevantEntries_ExcludesTermsNotPresentInSourceText()
    {
        var opts = OptionsWithGlossary(
            new GlossaryEntry("API", "接口"),
            new GlossaryEntry("blockchain", "区块链"));
        var sut = new InMemoryTranslationGlossary(opts);

        var results = sut.GetRelevantEntries("Call the API endpoint.", "en", "zh");

        Assert.Single(results);
        Assert.Equal("API", results[0].SourceTerm);
    }

    [Fact]
    public void GetRelevantEntries_RespectsLanguageConstraints()
    {
        var opts = OptionsWithGlossary(
            new GlossaryEntry("token", "令牌", SourceLanguage: "en", TargetLanguage: "zh"),
            new GlossaryEntry("token", "token", SourceLanguage: "en", TargetLanguage: "fr"));
        var sut = new InMemoryTranslationGlossary(opts);

        var zhResults = sut.GetRelevantEntries("The access token expired.", "en", "zh");
        var frResults = sut.GetRelevantEntries("The access token expired.", "en", "fr");

        Assert.Single(zhResults);
        Assert.Equal("令牌", zhResults[0].TargetTerm);
        Assert.Single(frResults);
        Assert.Equal("token", frResults[0].TargetTerm);
    }

    [Fact]
    public void GetRelevantEntries_NullLanguageConstraint_MatchesAnyLanguage()
    {
        var opts = OptionsWithGlossary(new GlossaryEntry("LiveLingo", "LiveLingo"));
        var sut = new InMemoryTranslationGlossary(opts);

        Assert.Single(sut.GetRelevantEntries("Use LiveLingo.", "en", "zh"));
        Assert.Single(sut.GetRelevantEntries("Use LiveLingo.", "en", "fr"));
        Assert.Single(sut.GetRelevantEntries("Use LiveLingo.", "ja", "ko"));
    }

    [Fact]
    public void GetRelevantEntries_CapsAtTwelveEntries()
    {
        var entries = Enumerable.Range(1, 20)
            .Select(i => new GlossaryEntry($"term{i}", $"翻译{i}"))
            .ToArray();
        var opts = OptionsWithGlossary(entries);
        var sut = new InMemoryTranslationGlossary(opts);

        var source = string.Join(" ", entries.Select(e => e.SourceTerm));
        var results = sut.GetRelevantEntries(source, "en", "zh");

        Assert.Equal(12, results.Count);
    }

    [Fact]
    public void GetRelevantEntries_ReflectsRuntimeGlossaryChanges()
    {
        var opts = new CoreOptions();
        opts.Glossary = [new GlossaryEntry("apple", "苹果")];
        var sut = new InMemoryTranslationGlossary(opts);

        Assert.Single(sut.GetRelevantEntries("I eat an apple.", "en", "zh"));

        // Simulate settings change updating CoreOptions.Glossary
        opts.Glossary = [];

        Assert.Empty(sut.GetRelevantEntries("I eat an apple.", "en", "zh"));
    }
}

public sealed class TranslationPromptBuilderGlossaryTests
{
    [Fact]
    public void BuildDefault_WithGlossaryHints_InjectsGlossaryInSystemMessage()
    {
        var hints = new List<GlossaryEntry>
        {
            new("API", "接口"),
            new("token", "令牌")
        };

        var messages = TranslationPromptBuilder.BuildDefault(
            "Call the API with a token.", "English", "Chinese", hints);

        var systemContent = messages.First(m => m.Role == "system").Content;
        Assert.Contains("[Glossary - translate these terms exactly as specified]:", systemContent);
        Assert.Contains("API → 接口", systemContent);
        Assert.Contains("token → 令牌", systemContent);
    }

    [Fact]
    public void Build_WithNullGlossary_DoesNotAddGlossarySection()
    {
        var messages = TranslationPromptBuilder.BuildDefault(
            "Hello world", "English", "Chinese", glossaryHints: null);

        var systemContent = messages.First(m => m.Role == "system").Content;
        Assert.DoesNotContain("Glossary", systemContent);
    }

    [Theory]
    [InlineData(LocalModelChatTemplate.Gemma)]
    [InlineData(LocalModelChatTemplate.Qwen)]
    [InlineData(LocalModelChatTemplate.Generic)]
    public void Build_AllTemplates_InjectGlossaryWhenProvided(LocalModelChatTemplate template)
    {
        var hints = new List<GlossaryEntry> { new("AI", "人工智能") };

        var messages = TranslationPromptBuilder.Build(
            "AI is transforming industries.", "English", "Chinese", template, hints);

        var systemContent = messages.First(m => m.Role == "system").Content;
        Assert.Contains("AI → 人工智能", systemContent);
    }
}
