using System.Net;
using System.Text;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using NSubstitute;

namespace LiveLingo.Core.Tests.Cloud;

public class RoutedChatClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Auth, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"cloud text\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8, "text/event-stream")
            };
        }
    }

    private static readonly IReadOnlyDictionary<string, object> Extra = new Dictionary<string, object> { ["top_k"] = 20 };

    private static (RoutedChatClient Client, IChatCompletionClient Local, StubHandler Handler, ICloudRoutes Routes) Create(CloudRoute? route)
    {
        var local = Substitute.For<IChatCompletionClient>();
        local.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion("local text", 1, 2, 3));
        var routes = Substitute.For<ICloudRoutes>();
        routes.GetAsync(InferenceRole.Translator, Arg.Any<CancellationToken>()).Returns(route);
        var handler = new StubHandler();
        return (new RoutedChatClient(InferenceRole.Translator, routes, local, new HttpClient(handler)), local, handler, routes);
    }

    [Fact]
    public async Task WithoutACloudRoute_TheLocalServerIsUsed_AndNothingLeavesTheMachine()
    {
        var (client, local, handler, _) = Create(route: null);

        var result = await client.CompleteAsync(new ChatRequest("p", 8, 0, Extra), CancellationToken.None);

        Assert.Equal("local text", result.Text);
        await local.Received(1).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WithACloudRoute_TheRequestGoesToTheEndpoint_WithKeyAndModel_AndNotToTheLocalServer()
    {
        var route = CloudRoute.Create("https://example.openai.azure.com/openai/v1", "gpt-4.1-mini", "sk-test");
        var (client, local, handler, _) = Create(route);

        var result = await client.CompleteAsync(new ChatRequest("翻译这个", 8, 0, Extra), CancellationToken.None);

        Assert.Equal("cloud text", result.Text);
        await local.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
        var (uri, auth, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://example.openai.azure.com/openai/v1/chat/completions", uri.ToString());
        Assert.Equal("Bearer sk-test", auth);
        Assert.Contains("\"model\":\"gpt-4.1-mini\"", body);
        Assert.DoesNotContain("top_k", body);
    }

    [Fact]
    public async Task TheRouteIsReadOnEveryCall_SoSettingsChangesApplyImmediately()
    {
        var route = CloudRoute.Create("https://api.example.com/v1", "m", "k");
        var (client, local, handler, routes) = Create(null);
        await client.CompleteAsync(new ChatRequest("p", 8, 0, Extra), CancellationToken.None);

        routes.GetAsync(InferenceRole.Translator, Arg.Any<CancellationToken>()).Returns(route);
        await client.CompleteAsync(new ChatRequest("p", 8, 0, Extra), CancellationToken.None);

        await local.Received(1).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ACloudFailure_IsReported_NotSilentlyRetriedLocally()
    {
        var route = CloudRoute.Create("https://api.example.com/v1", "m", "k");
        var local = Substitute.For<IChatCompletionClient>();
        var routes = Substitute.For<ICloudRoutes>();
        routes.GetAsync(InferenceRole.Translator, Arg.Any<CancellationToken>()).Returns(route);
        var failing = new HttpClient(new FailingHandler());
        var client = new RoutedChatClient(InferenceRole.Translator, routes, local, failing);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(new ChatRequest("p", 8, 0, Extra), CancellationToken.None));

        await local.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
