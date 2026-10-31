using System.Net;
using System.Text;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Tests.Inference;

public class OpenAiChatClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public bool? ConnectionClose { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            ConnectionClose = request.Headers.ConnectionClose;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static HttpResponseMessage Sse(params string[] events) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(string.Join("\n\n", events) + "\n\n", Encoding.UTF8, "text/event-stream")
    };

    private static string Delta(string text) =>
        "data: " + System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = text } } } });

    private const string Stop = "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}";
    private const string Usage = "data: {\"choices\":[],\"usage\":{\"completion_tokens\":7}}";

    private static OpenAiChatClient ClientFor(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9/") });

    private static readonly IReadOnlyDictionary<string, object> NoExtra = new Dictionary<string, object>();

    [Fact]
    public async Task CompleteAsync_ConcatenatesDeltas_AndReportsUsage()
    {
        var handler = new StubHandler(_ => Sse(Delta("Do not "), Delta("retry. "), Stop, Usage, "data: [DONE]"));

        var result = await ClientFor(handler).CompleteAsync(
            new ChatRequest("prompt", 64, 0, NoExtra), CancellationToken.None);

        Assert.Equal("Do not retry.", result.Text);
        Assert.Equal(7, result.OutputTokens);
        Assert.True(result.FirstVisibleMs <= result.TotalMs);
    }

    [Fact]
    public async Task CompleteAsync_SendsPromptAsOneUserMessage_WithExtraFields()
    {
        var handler = new StubHandler(_ => Sse(Delta("ok"), Stop, "data: [DONE]"));
        var extra = new Dictionary<string, object> { ["top_k"] = 20, ["cache_prompt"] = false };

        await ClientFor(handler).CompleteAsync(
            new ChatRequest("原文\n第二行", 32, 0.2, extra), CancellationToken.None);

        Assert.Equal("http://127.0.0.1:9/v1/chat/completions", handler.RequestUri!.ToString());
        using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal(32, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(20, root.GetProperty("top_k").GetInt32());
        Assert.False(root.GetProperty("cache_prompt").GetBoolean());
        var message = root.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("原文\n第二行", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ClosesTheConnection_SoAServerThatDropsIdleSocketsNeverBreaksTheNextRequest()
    {
        var handler = new StubHandler(_ => Sse(Delta("ok"), Stop, "data: [DONE]"));

        await ClientFor(handler).CompleteAsync(new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None);

        Assert.True(handler.ConnectionClose);
    }

    [Fact]
    public async Task CompleteAsync_SendsTheSystemMessageFirst_WhenGiven()
    {
        var handler = new StubHandler(_ => Sse(Delta("ok"), Stop, "data: [DONE]"));

        await ClientFor(handler).CompleteAsync(
            new ChatRequest("用户输入", 32, 0, NoExtra, System: "你是整理器"), CancellationToken.None);

        using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBody!);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("你是整理器", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("用户输入", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_RejectsAStreamThatDidNotStopNormally()
    {
        var truncated = new StubHandler(_ => Sse(Delta("Do not"), "data: [DONE]"));
        var cutOff = new StubHandler(_ => Sse(Delta("Do not"), "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}", "data: [DONE]"));
        var noDone = new StubHandler(_ => Sse(Delta("Do not"), Stop));

        foreach (var handler in new[] { truncated, cutOff, noDone })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ClientFor(handler).CompleteAsync(
                new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None));
        }
    }

    [Fact]
    public async Task CompleteAsync_RejectsBlankOutput()
    {
        var handler = new StubHandler(_ => Sse(Delta("  "), Stop, "data: [DONE]"));

        await Assert.ThrowsAsync<InvalidDataException>(() => ClientFor(handler).CompleteAsync(
            new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_SurfacesServerErrorsInTheStream()
    {
        var handler = new StubHandler(_ => Sse("data: {\"error\":{\"message\":\"boom\"}}"));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ClientFor(handler).CompleteAsync(
            new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None));

        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_RejectsMalformedEvents()
    {
        var handler = new StubHandler(_ => Sse("data: {not json"));

        await Assert.ThrowsAsync<InvalidDataException>(() => ClientFor(handler).CompleteAsync(
            new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_PropagatesHttpFailures()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => ClientFor(handler).CompleteAsync(
            new ChatRequest("p", 8, 0, NoExtra), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_HonorsCancellation()
    {
        var handler = new StubHandler(_ => Sse(Delta("x"), Stop, "data: [DONE]"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientFor(handler).CompleteAsync(
            new ChatRequest("p", 8, 0, NoExtra), cts.Token));
    }
}
