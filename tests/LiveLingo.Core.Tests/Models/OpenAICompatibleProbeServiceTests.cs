using System.Net;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models;

public sealed class OpenAICompatibleProbeServiceTests
{
    [Fact]
    public async Task GetModelCatalogAsync_ReadsAndSortsModels()
    {
        string? capturedAuth = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            capturedAuth = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "data": [
                        { "id": "z-model", "owned_by": "vendor-z" },
                        { "id": "a-model", "owned_by": "vendor-a" }
                      ]
                    }
                    """)
            };
        }));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        var result = await service.GetModelCatalogAsync(
            new CloudProviderProbeRequest("https://api.openai.com/v1", "sk-test"),
            CancellationToken.None);

        Assert.Equal("Bearer sk-test", capturedAuth);
        Assert.True(result.IsSupported);
        Assert.Collection(
            result.Models,
            first =>
            {
                Assert.Equal("a-model", first.Id);
                Assert.Equal("vendor-a", first.OwnedBy);
            },
            second =>
            {
                Assert.Equal("z-model", second.Id);
                Assert.Equal("vendor-z", second.OwnedBy);
            });
    }

    [Fact]
    public async Task GetModelCatalogAsync_ReturnsUnsupported_WhenEndpointMissing()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        var result = await service.GetModelCatalogAsync(
            new CloudProviderProbeRequest("https://gateway.example.com/v1", "sk-test"),
            CancellationToken.None);

        Assert.False(result.IsSupported);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task TestConnectionAsync_FallsBackToModelProbe_WhenCatalogUnsupported()
    {
        var requests = new List<HttpRequestMessage>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request);
            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"choices":[{"message":{"content":"OK"}}]}
                        """)
                };
        }));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new CloudProviderProbeRequest("https://gateway.example.com/v1", "sk-test", "gpt-4.1-mini"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ModelCount);
        Assert.Contains("does not expose a model catalog", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal(HttpMethod.Post, requests[1].Method);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsSuccessSummary()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    { "data": [ { "id": "gpt-4.1-mini" }, { "id": "gpt-4.1" } ] }
                    """)
            }));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new CloudProviderProbeRequest("https://api.openai.com/v1", "sk-test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.ModelCount);
        Assert.Contains("2", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenApiKeyMissing()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Should not send request")));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new CloudProviderProbeRequest("https://api.openai.com/v1", ""),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("API key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeModelAsync_PostsMinimalChatCompletionsPayload()
    {
        string? capturedAuth = null;
        string? capturedJson = null;
        string? requestUri = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            capturedAuth = request.Headers.Authorization?.ToString();
            requestUri = request.RequestUri?.ToString();
            capturedJson = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":"OK"}}]}
                    """)
            };
        }));
        var service = new OpenAICompatibleProbeService(http, NullLogger<OpenAICompatibleProbeService>.Instance);

        await service.ProbeModelAsync(
            new CloudProviderProbeRequest("https://api.openai.com/v1", "sk-test"),
            "gpt-4.1-mini",
            CancellationToken.None);

        Assert.Equal("Bearer sk-test", capturedAuth);
        Assert.Equal("https://api.openai.com/v1/chat/completions", requestUri);
        Assert.NotNull(capturedJson);
        Assert.Contains("\"model\":\"gpt-4.1-mini\"", capturedJson, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":1", capturedJson, StringComparison.Ordinal);
        Assert.Contains("\"stream\":false", capturedJson, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
