using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Inference;
using NSubstitute;

namespace LiveLingo.Core.Tests.Cleanup;

public class LlmTextCleanerTests
{
    private static (LlmTextCleaner Cleaner, List<ChatRequest> Seen) Create(string completionText)
    {
        var seen = new List<ChatRequest>();
        var client = Substitute.For<IChatCompletionClient>();
        client.CompleteAsync(Arg.Do<ChatRequest>(seen.Add), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatCompletion(completionText, 10, 20, 5));
        return (new LlmTextCleaner(client), seen);
    }

    [Fact]
    public void UserMessage_CarriesGlossaryContextTargetAndContinuation()
    {
        var message = LlmTextCleaner.BuildUserMessage(
            new CleanupRequest("呃，用 cancel token。", "先改接口。", Continues: true, ["CancellationToken", "Redis"]));

        Assert.Equal(
            "词表：CancellationToken、Redis\n前文（只读）：先改接口。\n目标：呃，用 cancel token。\n后文：（说话人还在继续说下去）",
            message);
    }

    [Fact]
    public void UserMessage_MarksMissingContextAndTheEndOfSpeech()
    {
        var message = LlmTextCleaner.BuildUserMessage(new CleanupRequest("好的。", "", Continues: false, []));

        Assert.Equal("前文（只读）：（无）\n目标：好的。\n后文：（无，说话已经结束）", message);
    }

    [Fact]
    public async Task CleanAsync_SendsTheSystemPrompt_WithDeterministicDecoding()
    {
        var (cleaner, seen) = Create("好的。");

        var result = await cleaner.CleanAsync(new CleanupRequest("呃，好的。", "", false, []), CancellationToken.None);

        Assert.Equal("好的。", result);
        var request = Assert.Single(seen);
        Assert.Equal(LlmTextCleaner.SystemPrompt, request.System);
        Assert.Equal(0, request.Temperature);
        Assert.Contains("只输出整理后的目标文字", request.System);
    }

    [Theory]
    [InlineData("好的。", "好的。")]
    [InlineData("  \n好的。\n", "好的。")]
    [InlineData("目标：好的。", "好的。")]
    [InlineData("“好的。”", "好的。")]
    [InlineData("\"好的。\"", "好的。")]
    public async Task CleanAsync_RemovesLabelsQuotesAndOuterWhitespace(string output, string expected)
    {
        var (cleaner, _) = Create(output);

        Assert.Equal(expected, await cleaner.CleanAsync(new CleanupRequest("好的。", "", false, []), CancellationToken.None));
    }

    [Fact]
    public async Task CleanAsync_BoundsTheOutputByTextLength_AndLeavesRoomForAnEchoedContext()
    {
        var (cleaner, seen) = Create("ok");

        await cleaner.CleanAsync(new CleanupRequest("好", "", false, []), CancellationToken.None);
        await cleaner.CleanAsync(new CleanupRequest("好", new string('前', 100), false, []), CancellationToken.None);
        await cleaner.CleanAsync(new CleanupRequest(new string('字', 1500), "", false, []), CancellationToken.None);

        Assert.Equal(1 * 2 + 48, seen[0].MaxTokens);
        Assert.Equal(101 * 2 + 48, seen[1].MaxTokens);
        Assert.Equal(1700, seen[2].MaxTokens);
    }

    [Theory]
    [InlineData("我觉得现在这个原文的识别和整理出来的文本。", "我觉得现在这个原文的识别和整理出来的文本都没有什么变化。", "都没有什么变化。")]
    [InlineData("你好吗。我觉得现在这个原文的识别。", "我觉得现在这个原文的识别，都没有什么变化。", "都没有什么变化。")]
    [InlineData("我觉得现在这个原文的识别。", "都没有什么变化。", "都没有什么变化。")]
    [InlineData("我觉得现在这个原文的识别。", "都没有什么变化，我觉得现在这个原文的识别。", "都没有什么变化，我觉得现在这个原文的识别。")]
    [InlineData("", "都没有什么变化。", "都没有什么变化。")]
    [InlineData("我觉得现在这个原文的识别。", "我觉得现在这个原文的识别。", "")]
    public void WithoutEchoedContext_RemovesWhatTheModelCopiedFromTheContext(string preceding, string output, string expected) =>
        Assert.Equal(expected, LlmTextCleaner.WithoutEchoedContext(output, preceding));

    [Fact]
    public async Task CleanAsync_DropsTheContextTheModelWroteBeforeItsAnswer()
    {
        var (cleaner, _) = Create("我们先把下载接口改掉。然后补测试。");

        var text = await cleaner.CleanAsync(new CleanupRequest("补测试。", "我们先把下载接口改掉。", true, []), CancellationToken.None);

        Assert.Equal("然后补测试。", text);
    }
}
