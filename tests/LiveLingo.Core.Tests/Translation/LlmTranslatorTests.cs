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
        Assert.Equal(false, request.Extra["cache_prompt"]);
        Assert.Equal(10, result.FirstVisibleMs);
        Assert.Equal(20, result.TotalMs);
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
}
