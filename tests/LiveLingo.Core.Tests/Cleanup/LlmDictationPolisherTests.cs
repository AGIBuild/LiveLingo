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

    [Fact]
    public async Task StructuredFactAnchorMode_UsesStructuredFrameSlightTemperatureAndRepairFeedback()
    {
        var client = Substitute.For<IChatCompletionClient>();
        ChatRequest? seen = null;
        client.CompleteAsync(Arg.Do<ChatRequest>(r => seen = r), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion("<think>reasoning</think>\n### 任务目标\n保留 `CacheService` 与 30 秒。", 0, 0, 12));
        var polisher = new LlmDictationPolisher(client);
        var request = new PolishRequest(
            "修一下 `CacheService` 超时 30 秒。",
            "",
            "",
            ["CacheService"],
            PolishPresets.AiTaskInstructions,
            Gate: PolishGateKind.FactAnchor,
            RepairFeedback: "上一版丢失了生效数字「30」");

        var output = await polisher.PolishAsync(request, CancellationToken.None);

        Assert.Equal("### 任务目标\n保留 `CacheService` 与 30 秒。", output);
        Assert.Contains(LlmDictationPolisher.StructuredFrame, seen!.System);
        Assert.DoesNotContain(LlmDictationPolisher.Frame, seen.System);
        Assert.Equal(0.1, seen.Temperature);
        Assert.Contains("校验修正要求：上一版丢失了生效数字「30」", seen.Prompt);
    }

    [Fact]
    public async Task Polish_StreamsCleanPartials_WhileHidingThinkBlocksAndMetaPrefixes()
    {
        var client = Substitute.For<IChatCompletionClient>();
        client.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<ChatRequest>();
                req.OnDelta?.Invoke("<thi");
                req.OnDelta?.Invoke("nk>internal reasoning");
                req.OnDelta?.Invoke("</think>\n目");
                req.OnDelta?.Invoke("标：### 任务目标\n");
                req.OnDelta?.Invoke("排查 `CacheService` 超时 30 秒。");
                return new ChatCompletion(
                    "<think>internal reasoning</think>\n目标：### 任务目标\n排查 `CacheService` 超时 30 秒。",
                    10,
                    25,
                    12);
            });
        var partials = new List<string>();
        var polisher = new LlmDictationPolisher(client);
        var request = new PolishRequest(
            "修一下 `CacheService` 超时 30 秒。",
            "",
            "",
            ["CacheService"],
            PolishPresets.AiTaskInstructions,
            Gate: PolishGateKind.FactAnchor,
            OnPartial: partials.Add);

        var final = await polisher.PolishAsync(request, CancellationToken.None);

        Assert.Equal([
            "### 任务目标\n",
            "### 任务目标\n排查 `CacheService` 超时 30 秒。"
        ], partials);
        Assert.All(partials, p => Assert.DoesNotContain("<think", p, StringComparison.OrdinalIgnoreCase));
        Assert.All(partials, p => Assert.DoesNotContain("目标：", p, StringComparison.Ordinal));
        Assert.Equal("### 任务目标\n排查 `CacheService` 超时 30 秒。", final);
    }
}
