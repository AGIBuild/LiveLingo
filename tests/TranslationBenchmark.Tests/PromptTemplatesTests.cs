using TranslationBenchmark;

namespace TranslationBenchmark.Tests;

public class PromptTemplatesTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("gemma")]
    [InlineData("gemma4-tagged")]
    [InlineData("gemma4-concise")]
    [InlineData("gemma4-structured")]
    [InlineData("minimal")]
    public void Build_ReturnsNonEmptyPromptsForAllKnownVariants(string variant)
    {
        var (system, user) = PromptTemplates.Build(variant, "Chinese", "English", "你好");

        Assert.False(string.IsNullOrWhiteSpace(system), $"system empty for {variant}");
        Assert.False(string.IsNullOrWhiteSpace(user), $"user empty for {variant}");
        Assert.Contains("你好", user);
    }

    [Fact]
    public void Build_UnknownVariant_FallsBackToDefault()
    {
        var expected = PromptTemplates.Default("Chinese", "English", "x");
        var actual = PromptTemplates.Build("does-not-exist", "Chinese", "English", "x");

        Assert.Equal(expected.System, actual.System);
        Assert.Equal(expected.User, actual.User);
    }

    [Fact]
    public void AllVariantNames_CoversEverySwitchArm()
    {
        // Every advertised variant must produce a distinct prompt — catches copy-paste regressions.
        var prompts = PromptTemplates.AllVariantNames
            .Select(v => PromptTemplates.Build(v, "Chinese", "English", "hi"))
            .ToList();

        var uniqueSystems = prompts.Select(p => p.System).Distinct().Count();
        Assert.Equal(prompts.Count, uniqueSystems);
    }

    [Fact]
    public void Gemma4Structured_UsesWrappingTagInUserMessage()
    {
        var (_, user) = PromptTemplates.Gemma4Structured("English", "Chinese", "sample");
        Assert.Contains("<t>", user);
    }
}
