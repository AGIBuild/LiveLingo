using LiveLingo.Core.Inference;
using LiveLingo.Core.Translation;
using NSubstitute;

namespace LiveLingo.Core.Tests.Translation;

public class LlmTranslatorTests
{
    private static (LlmTranslator Translator, List<ChatRequest> Seen) Create(string completionText)
    {
        var seen = new List<ChatRequest>();
        var client = Substitute.For<IChatCompletionClient>();
        client.CompleteAsync(Arg.Do<ChatRequest>(seen.Add), Arg.Any<CancellationToken>())
            .Returns(_ => new ChatCompletion(completionText, 10, 20, 5));
        return (new LlmTranslator(client), seen);
    }

    [Fact]
    public async Task TranslateAsync_SendsThePlainPrompt_WithDeterministicSampling()
    {
        var (translator, seen) = Create("Do not retry.");

        var result = await translator.TranslateAsync(
            new TranslationRequest("不要重试。", TargetLanguage.English, []), CancellationToken.None);

        Assert.Equal("Do not retry.", result.Text);
        var request = Assert.Single(seen);
        Assert.EndsWith(":\n\n不要重试。", request.Prompt);
        Assert.Equal(0, request.Temperature);
        Assert.Equal(20, request.Extra["top_k"]);
        Assert.Equal(true, request.Extra["cache_prompt"]);
        Assert.Equal(1.05, request.Extra["repeat_penalty"]);
        Assert.Equal(10, result.FirstVisibleMs);
        Assert.Equal(20, result.TotalMs);
        Assert.Null(request.System);
    }

    [Fact]
    public async Task TranslateAsync_ProtectsLiteralsAndRestoresThem()
    {
        var (translator, seen) = Create("Keep __KEEP_0__ unchanged.");

        var result = await translator.TranslateAsync(
            new TranslationRequest("保留 `CancellationToken` 不变。", TargetLanguage.English, []), CancellationToken.None);

        Assert.DoesNotContain("CancellationToken", seen[0].Prompt);
        Assert.Equal("Keep `CancellationToken` unchanged.", result.Text);
    }

    [Fact]
    public async Task TranslateAsync_FailsWhenAProtectedLiteralIsLost()
    {
        var (translator, _) = Create("Keep it unchanged.");

        await Assert.ThrowsAsync<InvalidDataException>(() => translator.TranslateAsync(
            new TranslationRequest("保留 `CancellationToken` 不变。", TargetLanguage.English, []), CancellationToken.None));
    }

    [Fact]
    public async Task TranslateAsync_BoundsOutputTokensBySourceLength()
    {
        var (translator, seen) = Create("ok");

        await translator.TranslateAsync(new TranslationRequest("好", TargetLanguage.English, []), CancellationToken.None);
        await translator.TranslateAsync(
            new TranslationRequest(new string('字', 5000), TargetLanguage.English, []), CancellationToken.None);

        Assert.Equal(1 * 2 + 128, seen[0].MaxTokens);
        Assert.Equal(2048, seen[1].MaxTokens);
    }

    [Fact]
    public async Task FinalDocument_UsesEditingRules_WithoutLivePromptChangeOrOutputTruncation()
    {
        var response = new string('字', 3000);
        var (translator, seen) = Create(response);
        var source = new string('x', 800);
        var result = await translator.TranslateAsync(
            new TranslationRequest(source, TargetLanguage.Chinese, [], FinalDocument: true), CancellationToken.None);
        Assert.Equal(response, result.Text);
        Assert.True(seen[0].MaxTokens > 2048);
        Assert.Contains("fluent, continuous Chinese", seen[0].Prompt);
        Assert.Contains("join false ASR sentence breaks", seen[0].Prompt);
        Assert.Contains("Do not summarize", seen[0].Prompt);
        Assert.Equal(1.05, seen[0].Extra["repeat_penalty"]);
        var preview = TranslationPrompt.Build(new TranslationRequest("你好。", TargetLanguage.Chinese, []));
        Assert.DoesNotContain("join false ASR", preview);
    }

    [Fact]
    public async Task EmptyResponse_IsExplicitFailure()
    {
        var (translator, _) = Create(" \n ");
        await Assert.ThrowsAsync<InvalidDataException>(() => translator.TranslateAsync(
            new TranslationRequest("你好。", TargetLanguage.Chinese, []), CancellationToken.None));
    }

    [Fact]
    public async Task FinalChineseSpeech_OmittedUncertaintyIsRejected_NotReturnedAsReadyText()
    {
        var (translator, seen) = Create("你在合适的位置提醒用户。");
        const string source = "我不确定什么地方合适，你给我找一个合适的地方，至少能够提示用户。";
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => translator.TranslateAsync(
            new TranslationRequest(source, TargetLanguage.Chinese, [], FinalDocument: true), CancellationToken.None));
        Assert.Contains("Final same-language translation refused", error.Message);
        Assert.Single(seen); // No hidden cleaner or recursive retry.
        // Live translation keeps its existing behavior; this guard belongs only to final document editing.
        var live = await translator.TranslateAsync(
            new TranslationRequest(source, TargetLanguage.Chinese, []), CancellationToken.None);
        Assert.Equal("你在合适的位置提醒用户。", live.Text);
    }

    [Fact]
    public async Task FinalChineseSpeech_FaithfulCleanupStillPasses()
    {
        var (translator, _) = Create("我不确定什么地方合适，请你找一个合适的位置，而不是只提示。");
        var result = await translator.TranslateAsync(new TranslationRequest(
            "呃，我不确定什么地方合适，请你找一个合适的位置，而不是只提示。",
            TargetLanguage.Chinese, [], FinalDocument: true), CancellationToken.None);
        Assert.DoesNotContain("呃", result.Text);
        Assert.Contains("我不确定", result.Text);
        Assert.Contains("而不是只提示", result.Text);
    }
}
