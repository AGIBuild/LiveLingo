using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Core.Tests.Models;

public sealed class DefaultModelInvocationServiceTests
{
    [Fact]
    public async Task InvokeAsync_uses_matching_runtime_and_provider()
    {
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        var runtime = Substitute.For<IModelRuntime>();
        var provider = Substitute.For<IModelProvider>();
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "http://127.0.0.1:5050");
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "prompt"), new ModelChatMessage("user", "text")],
            ModelInvocationOptions.CreateTranslationDefaults());

        runtime.RuntimeKind.Returns(ModelRuntimeKind.LlamaServer);
        provider.ProviderKind.Returns(ModelProviderKind.LlamaServer);
        runtime.AcquireSessionAsync(profile, ModelTaskType.Translation, Arg.Any<CancellationToken>())
            .Returns(session);
        provider.InvokeAsync(session, request, Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("translated"));

        var service = new DefaultModelInvocationService([runtime], [provider]);

        var result = await service.InvokeAsync(request, CancellationToken.None);

        Assert.Equal("translated", result.Text);
        await runtime.Received(1).AcquireSessionAsync(profile, ModelTaskType.Translation, Arg.Any<CancellationToken>());
        await provider.Received(1).InvokeAsync(session, request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_throws_when_matching_runtime_missing()
    {
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "prompt"), new ModelChatMessage("user", "text")],
            ModelInvocationOptions.CreateTranslationDefaults());

        var service = new DefaultModelInvocationService([], []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InvokeAsync(request, CancellationToken.None));

        Assert.Contains(profile.RuntimeKind.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_throws_when_matching_provider_missing()
    {
        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        var runtime = Substitute.For<IModelRuntime>();
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "http://127.0.0.1:5050");
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "prompt"), new ModelChatMessage("user", "text")],
            ModelInvocationOptions.CreateTranslationDefaults());

        runtime.RuntimeKind.Returns(ModelRuntimeKind.LlamaServer);
        runtime.AcquireSessionAsync(profile, ModelTaskType.Translation, Arg.Any<CancellationToken>())
            .Returns(session);

        var service = new DefaultModelInvocationService([runtime], []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InvokeAsync(request, CancellationToken.None));

        Assert.Contains(profile.ProviderKind.ToString(), ex.Message, StringComparison.Ordinal);
    }
}
