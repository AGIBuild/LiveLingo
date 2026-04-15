using System.Net;
using System.Text.Json;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models;

public sealed class LlamaServerChatProviderTests
{
    [Fact]
    public async Task InvokeAsync_posts_chat_request_and_reads_content_array_response()
    {
        string? capturedJson = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            capturedJson = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":[{"type":"text","text":"Hello world"}]}}]}
                    """)
            };
        }));

        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        var provider = new LlamaServerChatProvider(http, NullLogger<LlamaServerChatProvider>.Instance);
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "Translate"), new ModelChatMessage("user", "你好")],
            ModelInvocationOptions.CreateTranslationDefaults());
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "http://127.0.0.1:5050");

        var result = await provider.InvokeAsync(session, request, CancellationToken.None);

        Assert.Equal("Hello world", result.Text);
        Assert.NotNull(capturedJson);

        using var doc = JsonDocument.Parse(capturedJson!);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(512, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("stop").GetArrayLength());

        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Translate", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("你好", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task InvokeAsync_throws_when_assistant_output_is_empty()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"choices":[{"finish_reason":"stop","message":{"content":""}}]}
                """)
        }));

        var profile = new StaticModelCatalog().FindById(ModelRegistry.Qwen35_9B.Id)!;
        var provider = new LlamaServerChatProvider(http, NullLogger<LlamaServerChatProvider>.Instance);
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "Translate"), new ModelChatMessage("user", "你好")],
            ModelInvocationOptions.CreateTranslationDefaults());
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "http://127.0.0.1:5050");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InvokeAsync(session, request, CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
