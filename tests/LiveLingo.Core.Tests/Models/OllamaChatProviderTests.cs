using System.Net;
using System.Text;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models;

public sealed class OllamaChatProviderTests
{
    [Fact]
    public void ProviderKind_IsOllama()
    {
        var provider = CreateProvider(new FakeHandler(_ => CreateResponse("")));
        Assert.Equal(ModelProviderKind.Ollama, provider.ProviderKind);
    }

    [Fact]
    public async Task InvokeStreamingAsync_YieldsContentDeltasFromOllamaNdjson()
    {
        // Ollama streams NDJSON chunks, each containing a Message delta.
        var handler = new FakeHandler(_ => CreateResponse(
            """
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":"Hello"},"done":false}
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":" world"},"done":false}
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

            """));
        var provider = CreateProvider(handler);

        var deltas = new List<string>();
        await foreach (var delta in provider.InvokeStreamingAsync(
                           CreateSession(), CreateRequest(), CancellationToken.None))
        {
            deltas.Add(delta);
        }

        Assert.Contains("Hello", string.Concat(deltas));
        Assert.Contains("world", string.Concat(deltas));
    }

    [Fact]
    public async Task InvokeAsync_AggregatesStreamingDeltasIntoSingleResult()
    {
        var handler = new FakeHandler(_ => CreateResponse(
            """
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":"Bonjour"},"done":false}
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":" le monde"},"done":false}
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

            """));
        var provider = CreateProvider(handler);

        var result = await provider.InvokeAsync(
            CreateSession(), CreateRequest(), CancellationToken.None);

        Assert.Equal("Bonjour le monde", result.Text);
    }

    [Fact]
    public async Task InvokeStreamingAsync_ConcurrentCallsShareHttpClient_WithoutRace()
    {
        // Regression guard for the earlier HttpClient.BaseAddress-binding race:
        // a singleton OllamaChatProvider shares one HttpClient between concurrent
        // translations, so the previous "null-check then assign BaseAddress"
        // pattern could throw from HttpClient internals once the first request
        // had been dispatched. The rewritten provider uses the session endpoint
        // directly, so firing many concurrent streams against the same instance
        // must complete cleanly without mutating shared state.
        var handler = new FakeHandler(_ => CreateResponse(
            """
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":"ok"},"done":false}
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

            """));
        var provider = CreateProvider(handler);

        async Task<string> RunOnce()
        {
            var sb = new StringBuilder();
            await foreach (var d in provider.InvokeStreamingAsync(
                               CreateSession(), CreateRequest(), CancellationToken.None))
                sb.Append(d);
            return sb.ToString();
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RunOnce()));
        Assert.All(results, r => Assert.Equal("ok", r));
    }

    [Fact]
    public async Task InvokeAsync_Throws_WhenStreamProducesNoContent()
    {
        var handler = new FakeHandler(_ => CreateResponse(
            """
            {"model":"gemma3:4b","created_at":"2026-04-17T00:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}

            """));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.InvokeAsync(CreateSession(), CreateRequest(), CancellationToken.None));
    }

    private static OllamaChatProvider CreateProvider(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<OllamaChatProvider>.Instance);

    private static ModelRuntimeSession CreateSession() =>
        new(CreateProfile(), ModelTaskType.Translation, "http://localhost:11434");

    private static ModelProfile CreateProfile() =>
        new(
            "gemma3:4b",
            "Ollama gemma3:4b",
            ModelTaskType.Translation,
            ModelProviderKind.Ollama,
            ModelRuntimeKind.Ollama,
            ModelExecutionKind.ChatCompletions,
            [],
            new ModelDescriptor("gemma3:4b", "Ollama gemma3:4b", string.Empty, 0, ModelType.Translation),
            SupportsAllLanguages: true);

    private static ModelInvocationRequest CreateRequest() =>
        new(
            CreateProfile(),
            ModelTaskType.Translation,
            [new ModelChatMessage("user", "Translate to French: Hello world")],
            ModelInvocationOptions.CreateTranslationDefaults());

    private static HttpResponseMessage CreateResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson")
        };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
