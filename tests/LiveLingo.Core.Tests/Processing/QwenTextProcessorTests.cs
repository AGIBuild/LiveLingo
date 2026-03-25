using System.Net;
using System.Text.Json;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public sealed class QwenTextProcessorTests
{
    [Fact]
    public async Task ProcessAsync_posts_shared_chat_request_and_parses_content_array_response()
    {
        string? capturedJson = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            capturedJson = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":[{"type":"text","text":"processed"}]}}]}
                    """)
            };
        }));

        using var host = CreateLoadedHost();
        var processor = new TestProcessor(host, http);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("processed", result);
        Assert.NotNull(capturedJson);

        using var doc = JsonDocument.Parse(capturedJson!);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(512, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("stop").GetArrayLength());

        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Optimize the text. Do not use <think> tags.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("raw", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProcessAsync_returns_original_text_when_assistant_output_is_empty()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"choices":[{"message":{"content":""}}]}
                """)
        }));

        using var host = CreateLoadedHost();
        var processor = new TestProcessor(host, http);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("raw", result);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_text_when_transport_fails()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("boom")));

        using var host = CreateLoadedHost();
        var processor = new TestProcessor(host, http);

        var result = await processor.ProcessAsync("raw", "en", CancellationToken.None);

        Assert.Equal("raw", result);
    }

    private static QwenModelHost CreateLoadedHost()
    {
        var modelManager = Substitute.For<IModelManager>();
        var serverManager = Substitute.For<ILlamaServerProcessManager>();
        serverManager.State.Returns(ModelLoadState.Loaded);
        serverManager.CurrentEndpointUrl.Returns("http://127.0.0.1:5050");
        serverManager.StopServerAsync().Returns(Task.CompletedTask);

        return new QwenModelHost(
            modelManager,
            serverManager,
            Options.Create(new CoreOptions()),
            NullLogger<QwenModelHost>.Instance);
    }

    private sealed class TestProcessor(QwenModelHost host, HttpClient http)
        : QwenTextProcessor(host, http, NullLogger.Instance)
    {
        public override string Name => "optimize";
        protected override string SystemPrompt => "Optimize the text.";
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
