using LiveLingo.Core;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Models;

public sealed class OllamaRuntimeTests
{
    [Fact]
    public async Task AcquireSessionAsync_ReturnsConfiguredBaseUrl()
    {
        var runtime = CreateRuntime("http://localhost:11434");
        var profile = CreateProfile("gemma3:4b");

        var session = await runtime.AcquireSessionAsync(profile, ModelTaskType.Translation);

        Assert.Equal("http://localhost:11434", session.Endpoint);
        Assert.Same(profile, session.Profile);
        Assert.Equal(ModelTaskType.Translation, session.TaskType);
    }

    [Fact]
    public async Task AcquireSessionAsync_TrimsBaseUrl()
    {
        var runtime = CreateRuntime("  http://remote-host:11434/  ");
        var profile = CreateProfile("gemma3:4b");

        var session = await runtime.AcquireSessionAsync(profile, ModelTaskType.Translation);

        Assert.Equal("http://remote-host:11434/", session.Endpoint);
    }

    [Fact]
    public async Task AcquireSessionAsync_Throws_WhenBaseUrlEmpty()
    {
        var runtime = CreateRuntime("   ");
        var profile = CreateProfile("gemma3:4b");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.AcquireSessionAsync(profile, ModelTaskType.Translation));
    }

    [Fact]
    public void RuntimeKind_IsOllama()
    {
        var runtime = CreateRuntime("http://localhost:11434");
        Assert.Equal(ModelRuntimeKind.Ollama, runtime.RuntimeKind);
    }

    private static OllamaRuntime CreateRuntime(string baseUrl)
    {
        var options = Options.Create(new CoreOptions { OllamaBaseUrl = baseUrl });
        return new OllamaRuntime(options);
    }

    private static ModelProfile CreateProfile(string id) =>
        new(
            id,
            $"Ollama {id}",
            ModelTaskType.Translation,
            ModelProviderKind.Ollama,
            ModelRuntimeKind.Ollama,
            ModelExecutionKind.ChatCompletions,
            [],
            new ModelDescriptor(id, $"Ollama {id}", string.Empty, 0, ModelType.Translation),
            SupportsAllLanguages: true);
}
