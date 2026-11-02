using System.Net;
using System.Text;

namespace TranslationPoc.Tests;

public class HttpTests
{
    private const string GoodStream = """
        data: {"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}

        data: {"choices":[{"delta":{"content":" world"},"finish_reason":null}]}

        data: {"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"completion_tokens":2}}

        data: [DONE]

        """;

    [Fact]
    public async Task Joins_streamed_deltas_and_measures_visible_output()
    {
        var translator = New(GoodStream);
        var result = await translator.TranslateAsync(new Request("你好", "en", []), default);
        Assert.Equal("Hello world", result.Text);
        Assert.Equal(2, result.OutputTokens);
        Assert.True(result.TotalMs >= result.FirstVisibleMs);
    }

    [Theory]
    [InlineData("data: [DONE]\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":null}]}\n")]
    public async Task Empty_truncated_or_interrupted_stream_is_not_translation_success(string stream)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            New(stream).TranslateAsync(new Request("你好", "en", []), default));
    }

    [Fact]
    public async Task Http_failure_is_explicit()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            New("server unavailable", HttpStatusCode.ServiceUnavailable)
                .TranslateAsync(new Request("你好", "en", []), default));
    }

    [Fact]
    public async Task Output_budget_scales_with_source_length_so_runaway_generation_is_bounded()
    {
        using var client = new HttpClient(new Handler(GoodStream, HttpStatusCode.OK, request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            Assert.Contains("\"max_tokens\":132", body);
        }))
        { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        await new HttpTranslator(client).TranslateAsync(new Request("你好", "en", []), default);
    }

    [Fact]
    public async Task Native_server_requests_have_an_explicit_content_length()
    {
        using var client = new HttpClient(new Handler(GoodStream, HttpStatusCode.OK, request =>
            Assert.NotNull(request.Content?.Headers.ContentLength)))
        { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        await new HttpTranslator(client).TranslateAsync(new Request("你好", "en", []), default);
    }

    [Fact]
    public async Task Loopback_requests_explicitly_close_native_keepalive_connections()
    {
        using var client = new HttpClient(new Handler(GoodStream, HttpStatusCode.OK, request =>
            Assert.True(request.Headers.ConnectionClose)))
        { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        await new HttpTranslator(client).TranslateAsync(new Request("你好", "en", []), default);
    }

    [Fact]
    public async Task Source_text_is_sent_verbatim_and_the_reply_is_plain_text()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        using var client = new HttpClient(new Handler(stream, HttpStatusCode.OK, request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.DoesNotContain("json_schema", body);
        })) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        var result = await new HttpTranslator(client).TranslateAsync(new Request("你好", "en", []), default);
        Assert.Equal("Hello", result.Text);
    }

    private static HttpTranslator New(string stream, HttpStatusCode code = HttpStatusCode.OK) =>
        new(new HttpClient(new Handler(stream, code)) { BaseAddress = new Uri("http://127.0.0.1:8080/") });

    private sealed class Handler(string stream, HttpStatusCode code, Action<HttpRequestMessage>? inspect = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            inspect?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(stream, Encoding.UTF8, "text/event-stream")
            });
        }
    }
}
