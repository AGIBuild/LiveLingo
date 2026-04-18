using System.Net;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Models;

public sealed class OllamaProbeServiceTests
{
    [Fact]
    public async Task GetModelCatalogAsync_ParsesAndSortsModels()
    {
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "models": [
                        { "name": "qwen3:4b", "size": 3000000000, "digest": "abc", "modified_at": "2025-01-05T10:00:00Z" },
                        { "name": "gemma3:4b", "size": 4200000000, "digest": "def", "modified_at": "2025-02-10T12:30:00Z" }
                      ]
                    }
                    """)
            };
        }));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        var result = await service.GetModelCatalogAsync(
            new OllamaProbeRequest("http://localhost:11434/"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("http://localhost:11434/api/tags", captured.RequestUri!.ToString());
        Assert.Collection(
            result.Models,
            first =>
            {
                Assert.Equal("gemma3:4b", first.Id);
                Assert.Equal(4_200_000_000L, first.SizeBytes);
                Assert.Equal("def", first.Digest);
                Assert.NotNull(first.ModifiedAt);
            },
            second =>
            {
                Assert.Equal("qwen3:4b", second.Id);
                Assert.Equal(3_000_000_000L, second.SizeBytes);
            });
    }

    [Fact]
    public async Task GetModelCatalogAsync_ReturnsEmpty_WhenNoModelsField()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "other": "field" }""")
        }));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        var result = await service.GetModelCatalogAsync(
            new OllamaProbeRequest("http://localhost:11434"),
            CancellationToken.None);

        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task GetModelCatalogAsync_Throws_WhenBaseUrlEmpty()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModelCatalogAsync(
            new OllamaProbeRequest(""),
            CancellationToken.None));
    }

    [Fact]
    public async Task GetModelCatalogAsync_Throws_WhenDaemonReturnsNotFound()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModelCatalogAsync(
            new OllamaProbeRequest("http://localhost:11434"),
            CancellationToken.None));
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsSuccess_WhenModelsExist()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "models": [ { "name": "gemma3:4b", "size": 100 } ] }""")
        }));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new OllamaProbeRequest("http://localhost:11434"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ModelCount);
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsNonFatal_WhenNoPulledModels()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "models": [] }""")
        }));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new OllamaProbeRequest("http://localhost:11434"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("pulled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.ModelCount);
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsFailure_WhenDaemonUnreachable()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException("Connection refused")));
        var service = new OllamaProbeService(http, NullLogger<OllamaProbeService>.Instance);

        var result = await service.TestConnectionAsync(
            new OllamaProbeRequest("http://localhost:11434"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Connection refused", result.Message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
