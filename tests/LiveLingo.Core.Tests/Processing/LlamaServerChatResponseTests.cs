using System.Text.Json;
using LiveLingo.Core.Processing;
using Xunit;

namespace LiveLingo.Core.Tests.Processing;

public sealed class LlamaServerChatResponseTests
{
    [Fact]
    public void GetAssistantText_prefers_content_when_present()
    {
        const string json = """
            {"choices":[{"message":{"content":"Hello","reasoning_content":"think"}}]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Hello", LlamaServerChatResponse.GetAssistantText(doc.RootElement));
    }

    [Fact]
    public void GetAssistantText_falls_back_to_reasoning_content_when_content_empty()
    {
        const string json = """
            {"choices":[{"message":{"content":"","reasoning_content":"Final answer"}}]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Final answer", LlamaServerChatResponse.GetAssistantText(doc.RootElement));
    }

    [Fact]
    public void StripQwenThinkTags_keeps_text_after_last_closing_tag()
    {
        const string raw = "<think>\nx\n</think>\nTranslated";
        Assert.Equal("Translated", LlamaServerChatResponse.StripQwenThinkTags(raw));
    }

    [Fact]
    public void StripQwenThinkTags_strips_opening_only_when_no_closing_tag()
    {
        const string raw = "<think>\nTranslated without closing tag";
        Assert.Equal("Translated without closing tag", LlamaServerChatResponse.StripQwenThinkTags(raw));
    }

    [Fact]
    public void GetAssistantText_reads_string_from_content_array()
    {
        const string json = """
            {"choices":[{"message":{"content":[{"type":"text","text":"Hi"}]}}]}
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Hi", LlamaServerChatResponse.GetAssistantText(doc.RootElement));
    }
}
