using System.Net;
using System.Text;

namespace LiveLingo.HfGguf.Tests;

public class HfApiFileListerTests
{
    [Fact]
    public async Task ListGgufPathsAsync_ParsesFiles_AndSorts()
    {
        const string json = """[{"type":"file","path":"z.gguf"},{"type":"dir","path":"x"},{"type":"file","path":"a.gguf"}]""";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var lister = new HfApiFileLister(http, "https://huggingface.co");

        var paths = await lister.ListGgufPathsAsync("ns/repo", "main", null, CancellationToken.None);

        Assert.Equal(new[] { "a.gguf", "z.gguf" }, paths);
        Assert.Contains("/api/models/ns/repo/tree/main", handler.LastRequestUri?.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public Uri? LastRequestUri { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_factory(request));
        }
    }
}
