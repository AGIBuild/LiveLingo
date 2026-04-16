using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Engines;

public sealed class LlamaTranslationEngineTests
{
    [Fact]
    public async Task TranslateAsync_sends_correct_messages_to_chat_client()
    {
        var fake = new FakeChatClient("Hello world");
        var engine = new LlamaTranslationEngine(fake, NullLogger<LlamaTranslationEngine>.Instance);

        var translated = await engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None);

        Assert.Equal("Hello world", translated);
        Assert.NotNull(fake.CapturedMessages);

        var system = fake.CapturedMessages![0];
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("translate the source text from Chinese to English", system.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not use <think> tags", system.Text, StringComparison.Ordinal);

        var user = fake.CapturedMessages[1];
        Assert.Equal(ChatRole.User, user.Role);
        Assert.Contains("<source>\n你好世界\n</source>", user.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_passes_routing_hints_via_additional_properties()
    {
        var fake = new FakeChatClient("ok");
        var engine = new LlamaTranslationEngine(fake, NullLogger<LlamaTranslationEngine>.Instance);

        await engine.TranslateAsync("test", "zh", "en", CancellationToken.None);

        var props = fake.CapturedOptions?.AdditionalProperties;
        Assert.NotNull(props);
        Assert.Equal("zh", props!["sourceLang"]);
        Assert.Equal("en", props["targetLang"]);
        Assert.Equal("Translation", props["taskType"]);
        Assert.Equal(4, (int)props["textLength"]!);
    }

    [Fact]
    public async Task TranslateAsync_throws_when_response_is_empty()
    {
        var fake = new FakeChatClient("   ");
        var engine = new LlamaTranslationEngine(fake, NullLogger<LlamaTranslationEngine>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.TranslateAsync("你好世界", "zh", "en", CancellationToken.None));
    }

    [Theory]
    [InlineData("zh", "en", true)]
    [InlineData("en", "zh", true)]
    [InlineData("zh", "it", false)]
    public void SupportsLanguagePair_matches_registry(string sourceLanguage, string targetLanguage, bool expected)
    {
        var engine = new LlamaTranslationEngine(new FakeChatClient(""), NullLogger<LlamaTranslationEngine>.Instance);
        Assert.Equal(expected, engine.SupportsLanguagePair(sourceLanguage, targetLanguage));
    }

    private sealed class FakeChatClient(string responseText) : IChatClient
    {
        public IList<ChatMessage>? CapturedMessages { get; private set; }
        public ChatOptions? CapturedOptions { get; private set; }

        public ChatClientMetadata Metadata { get; } = new("fake");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedMessages = chatMessages.ToList();
            CapturedOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() { }
    }
}
