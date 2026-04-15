using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public sealed class QwenTextProcessorTests
{
    [Fact]
    public async Task ProcessAsync_builds_post_processing_invocation_request()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen25_15B.Id)!;
        ModelInvocationRequest? capturedRequest = null;
        selector.SelectPostProcessingProfile().Returns(profile);
        invocationService.InvokeAsync(Arg.Do<ModelInvocationRequest>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("processed"));

        var processor = new TestProcessor(selector, invocationService);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("processed", result);
        Assert.NotNull(capturedRequest);
        Assert.Same(profile, capturedRequest!.Profile);
        Assert.Equal(ModelTaskType.PostProcessing, capturedRequest.TaskType);
        Assert.Equal(512, capturedRequest.Options.MaxTokens);
        Assert.Equal(0.3f, capturedRequest.Options.Temperature);
        Assert.Equal(0.9f, capturedRequest.Options.TopP);
        Assert.Equal(ModelInvocationOptions.DefaultStopSequences, capturedRequest.Options.StopSequences);
        Assert.False(capturedRequest.Options.Stream);
        Assert.Collection(
            capturedRequest.Messages,
            system =>
            {
                Assert.Equal("system", system.Role);
                Assert.Equal("Optimize the text. Do not use <think> tags.", system.Content);
            },
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal("raw", user.Content);
            });
    }

    [Fact]
    public async Task ProcessAsync_returns_original_text_when_invocation_output_is_empty()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen25_15B.Id)!;
        selector.SelectPostProcessingProfile().Returns(profile);
        invocationService.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult(""));

        var processor = new TestProcessor(selector, invocationService);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("raw", result);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_text_when_invocation_fails()
    {
        var selector = Substitute.For<IModelSelector>();
        var invocationService = Substitute.For<IModelInvocationService>();
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen25_15B.Id)!;
        selector.SelectPostProcessingProfile().Returns(profile);
        invocationService.InvokeAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ModelInvocationResult>>(_ => throw new InvalidOperationException("boom"));

        var processor = new TestProcessor(selector, invocationService);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("raw", result);
    }

    private sealed class TestProcessor(IModelSelector selector, IModelInvocationService invocationService)
        : QwenTextProcessor(selector, invocationService, NullLogger.Instance)
    {
        public override string Name => "optimize";
        protected override string SystemPrompt => "Optimize the text.";
    }
}
