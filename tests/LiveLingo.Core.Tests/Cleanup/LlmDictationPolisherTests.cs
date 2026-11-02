using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Inference;
using NSubstitute;

namespace LiveLingo.Core.Tests.Cleanup;

public class LlmDictationPolisherTests
{
    [Fact]
    public async Task Polish_UsesFixedRulesAndCustomStyle_AlongsideTheOriginalText()
    {
        var client = Substitute.For<IChatCompletionClient>();
        ChatRequest? seen = null;
        client.CompleteAsync(Arg.Do<ChatRequest>(r => seen = r), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion("目标：先改接口，再补测试。", 0, 0, 10));
        var polisher = new LlmDictationPolisher(client);
        var request = new PolishRequest("先改接口。再补测试。", "", "Topic: tests.", ["Redis"], "Keep a friendly tone.");
        Assert.Equal("先改接口，再补测试。", await polisher.PolishAsync(request, CancellationToken.None));
        Assert.Contains(LlmDictationPolisher.Frame, seen!.System);
        Assert.Contains(request.Style, seen.System);
        Assert.Contains("全文摘要（只读）：Topic: tests.", seen.Prompt);
        Assert.Contains("词表：Redis", seen.Prompt);
        Assert.Contains(request.Target, seen.Prompt);
    }

    [Fact]
    public async Task Summary_CoversTheTailOfLongDictation_NotJustTheFirstChunk()
    {
        var client = Substitute.For<IChatCompletionClient>();
        var seen = new List<ChatRequest>();
        client.CompleteAsync(Arg.Do<ChatRequest>(seen.Add), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion("Summary.", 0, 0, 5));
        var text = new string('字', LlmDictationPolisher.MaxSummaryInputChars + 10) + "Tail requirement.";
        await new LlmDictationPolisher(client).SummarizeAsync(text, CancellationToken.None);
        Assert.Contains(seen, r => r.Prompt.Contains("Tail requirement.", StringComparison.Ordinal));
        Assert.All(seen, r => Assert.True(r.Prompt.Length <= LlmDictationPolisher.MaxSummaryInputChars + 10));
    }
}
