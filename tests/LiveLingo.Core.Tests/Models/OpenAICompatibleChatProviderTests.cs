using System.Net;
using System.Text.Json;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Models;

public sealed class OpenAICompatibleChatProviderTests
{
    [Fact]
    public async Task InvokeAsync_posts_openai_compatible_request_with_auth_header()
    {
        string? capturedJson = null;
        string? capturedAuth = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            capturedJson = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedAuth = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"choices":[{"message":{"content":"Bonjour"}}]}
                    """)
            };
        }));

        var profile = new ModelProfile(
            "gpt-4.1-mini",
            "Cloud gpt-4.1-mini",
            ModelTaskType.Translation,
            ModelProviderKind.OpenAICompatible,
            ModelRuntimeKind.RemoteHttp,
            ModelExecutionKind.ChatCompletions,
            [],
            new ModelDescriptor("gpt-4.1-mini", "Cloud gpt-4.1-mini", string.Empty, 0, ModelType.Translation),
            SupportsAllLanguages: true);
        var provider = new OpenAICompatibleChatProvider(
            http,
            Options.Create(new CoreOptions { CloudProviderApiKey = "sk-test" }),
            NullLogger<OpenAICompatibleChatProvider>.Instance);
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "Translate"), new ModelChatMessage("user", "你好")],
            ModelInvocationOptions.CreateTranslationDefaults());
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "https://api.openai.com/v1");

        var result = await provider.InvokeAsync(session, request, CancellationToken.None);

        Assert.Equal("Bonjour", result.Text);
        Assert.Equal("Bearer sk-test", capturedAuth);
        Assert.NotNull(capturedJson);

        using var doc = JsonDocument.Parse(capturedJson!);
        Assert.Equal("gpt-4.1-mini", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal(512, doc.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.95, doc.RootElement.GetProperty("top_p").GetDouble(), 3);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_throws_when_api_key_missing()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Should not send request")));
        var profile = new ModelProfile(
            "gpt-4.1-mini",
            "Cloud gpt-4.1-mini",
            ModelTaskType.Translation,
            ModelProviderKind.OpenAICompatible,
            ModelRuntimeKind.RemoteHttp,
            ModelExecutionKind.ChatCompletions,
            [],
            new ModelDescriptor("gpt-4.1-mini", "Cloud gpt-4.1-mini", string.Empty, 0, ModelType.Translation),
            SupportsAllLanguages: true);
        var provider = new OpenAICompatibleChatProvider(
            http,
            Options.Create(new CoreOptions()),
            NullLogger<OpenAICompatibleChatProvider>.Instance);
        var request = new ModelInvocationRequest(
            profile,
            ModelTaskType.Translation,
            [new ModelChatMessage("system", "Translate"), new ModelChatMessage("user", "你好")],
            ModelInvocationOptions.CreateTranslationDefaults());
        var session = new ModelRuntimeSession(profile, ModelTaskType.Translation, "https://api.openai.com/v1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.InvokeAsync(session, request, CancellationToken.None));

        Assert.Contains("API key", ex.Message, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
