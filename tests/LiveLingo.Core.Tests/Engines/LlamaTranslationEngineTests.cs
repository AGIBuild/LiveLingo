using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Engines;

public sealed class LlamaTranslationEngineTests
{
    [Fact]
    public async Task TranslateAsync_builds_translation_invocation_request()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        ModelInvocationRequest? capturedRequest = null;
        selector.SelectTranslationProfile("zh", "en").Returns(profile);
        invocationService.InvokeAsync(Arg.Do<ModelInvocationRequest>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("Hello world"));

        var engine = new LlamaTranslationEngine(selector, invocationService, NullLogger<LlamaTranslationEngine>.Instance);

        var translated = await engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None);

        Assert.Equal("Hello world", translated);
        Assert.NotNull(capturedRequest);
        Assert.Same(profile, capturedRequest!.Profile);
        Assert.Equal(ModelTaskType.Translation, capturedRequest.TaskType);
        Assert.Equal(512, capturedRequest.Options.MaxTokens);
        Assert.Equal(0.1f, capturedRequest.Options.Temperature);
        Assert.Equal(0.95f, capturedRequest.Options.TopP);
        Assert.Equal(ModelInvocationOptions.DefaultStopSequences, capturedRequest.Options.StopSequences);
        Assert.False(capturedRequest.Options.Stream);
        Assert.Collection(
            capturedRequest.Messages,
            system =>
            {
                Assert.Equal("system", system.Role);
                Assert.Contains("translate the source text from Chinese to English", system.Content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Do not use <think> tags", system.Content, StringComparison.Ordinal);
            },
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Contains("<source>\n你好世界\n</source>", user.Content, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task TranslateAsync_throws_when_invocation_output_is_empty()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        selector.SelectTranslationProfile("zh", "en").Returns(profile);
        invocationService.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult(""));

        var engine = new LlamaTranslationEngine(selector, invocationService, NullLogger<LlamaTranslationEngine>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None));
    }

    [Theory]
    [InlineData("zh", "en", true)]
    [InlineData("en", "zh", true)]
    [InlineData("zh", "it", false)]
    public void SupportsLanguagePair_matches_registry(string sourceLanguage, string targetLanguage, bool expected)
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var engine = new LlamaTranslationEngine(selector, invocationService, NullLogger<LlamaTranslationEngine>.Instance);

        Assert.Equal(expected, engine.SupportsLanguagePair(sourceLanguage, targetLanguage));
    }
}
