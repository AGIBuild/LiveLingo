using LiveLingo.Core.Processing;

namespace LiveLingo.Core.Tests.Processing;

public sealed class LlamaServerChatRequestTests
{
    [Fact]
    public void CreateTranslationRequest_builds_non_streaming_chat_completion_with_shared_defaults()
    {
        var request = LlamaServerChatRequest.CreateTranslation(
            "你好，世界",
            sourceLanguageName: "Chinese",
            targetLanguageName: "English");

        Assert.Collection(
            request.Messages,
            system =>
            {
                Assert.Equal("system", system.Role);
                Assert.Contains("translate the source text from Chinese to English", system.Content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Do not use <think> tags", system.Content, StringComparison.Ordinal);
            },
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Contains("<source>\n你好，世界\n</source>", user.Content, StringComparison.Ordinal);
            });

        Assert.Equal(512, request.MaxTokens);
        Assert.Equal(0.1f, request.Temperature);
        Assert.Equal(0.95f, request.TopP);
        Assert.Equal(LlamaServerChatRequest.DefaultStopSequences, request.Stop);
        Assert.False(request.Stream);
    }

    [Fact]
    public void CreateTextProcessorRequest_reuses_shared_stop_sequences()
    {
        var request = LlamaServerChatRequest.CreateTextProcessor(
            "Summarize the text.",
            "hello");

        Assert.Collection(
            request.Messages,
            system =>
            {
                Assert.Equal("system", system.Role);
                Assert.Equal("Summarize the text. Do not use <think> tags.", system.Content);
            },
            user =>
            {
                Assert.Equal("user", user.Role);
                Assert.Equal("hello", user.Content);
            });

        Assert.Equal(512, request.MaxTokens);
        Assert.Equal(0.3f, request.Temperature);
        Assert.Equal(0.9f, request.TopP);
        Assert.Equal(LlamaServerChatRequest.DefaultStopSequences, request.Stop);
        Assert.False(request.Stream);
    }
}
