using System.Net;
using System.Text.Json;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Engines;

public sealed class LlamaTranslationEngineTests
{
    [Fact]
    public async Task TranslateAsync_posts_shared_chat_request_and_reads_content_array_response()
    {
        string? capturedJson = null;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            capturedJson = _.Content is null ? null : _.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":[{"type":"text","text":"Hello world"}]}}]}
                    """)
            };
        }));

        using var host = CreateLoadedHost();
        var engine = new LlamaTranslationEngine(host, http, NullLogger<LlamaTranslationEngine>.Instance);

        var translated = await engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None);

        Assert.Equal("Hello world", translated);
        Assert.NotNull(capturedJson);

        using var doc = JsonDocument.Parse(capturedJson!);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(512, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("stop").GetArrayLength());
        Assert.Contains("</think>", doc.RootElement.GetProperty("stop").EnumerateArray().Select(x => x.GetString()));

        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("Do not use <think> tags", messages[0].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Contains("<source>\n你好世界\n</source>", messages[1].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_throws_when_assistant_output_is_empty()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"choices":[{"finish_reason":"stop","message":{"content":""}}]}
                """)
        }));

        using var host = CreateLoadedHost();
        var engine = new LlamaTranslationEngine(host, http, NullLogger<LlamaTranslationEngine>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None));
    }

    [Theory]
    [InlineData("zh", "en", true)]
    [InlineData("en", "zh", true)]
    [InlineData("zh", "it", false)]
    public void SupportsLanguagePair_matches_registry(string sourceLanguage, string targetLanguage, bool expected)
    {
        using var host = CreateLoadedHost();
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var engine = new LlamaTranslationEngine(host, http, NullLogger<LlamaTranslationEngine>.Instance);

        Assert.Equal(expected, engine.SupportsLanguagePair(sourceLanguage, targetLanguage));
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
