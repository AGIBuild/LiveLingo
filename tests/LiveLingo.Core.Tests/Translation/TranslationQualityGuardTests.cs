using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace LiveLingo.Core.Tests.Translation;

public sealed class TranslationQualityGuardTests
{
    [Fact]
    public void Check_EmptyTranslation_Fails()
    {
        var result = TranslationQualityGuard.Check("Hello world", "   ", "en", "zh");
        Assert.False(result.IsAcceptable);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Check_NormalTranslation_Passes()
    {
        var result = TranslationQualityGuard.Check("Hello world", "你好世界", "en", "zh");
        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Check_OmissionGuard_FailsWhenLongSourceHasTinyOutput()
    {
        var source = new string('a', 50); // 50 chars ≥ threshold
        var result = TranslationQualityGuard.Check(source, "ok", "en", "fr");
        Assert.False(result.IsAcceptable);
        Assert.Contains("too short", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_LengthRatio_NotApplied_ForCjkPairs()
    {
        // Chinese→English: char-level compression is expected
        var result = TranslationQualityGuard.Check("你好世界，今天天气真的很好啊！", "Hello world!", "zh", "en");
        Assert.True(result.IsAcceptable);
    }

    [Theory]
    [InlineData("The price is $1,234.56", "Le prix est 1,234.56 dollars", "en", "fr", true)]
    [InlineData("On 2025-01-15 the value was 42", "On 2025-01-15 the value was 99", "en", "fr", false)]
    public void Check_NumericFidelity(string source, string translation, string src, string tgt, bool shouldPass)
    {
        var result = TranslationQualityGuard.Check(source, translation, src, tgt);
        Assert.Equal(shouldPass, result.IsAcceptable);
    }

    [Fact]
    public void Check_PathologicalRepetition_Fails()
    {
        var repetitive = string.Join(" ", Enumerable.Repeat("the", 20));
        var result = TranslationQualityGuard.Check("A sentence.", repetitive, "en", "fr");
        Assert.False(result.IsAcceptable);
        Assert.Contains("repetition", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_ShortSource_ShortOutput_Passes()
    {
        // Short sources → short outputs are fine (below threshold)
        var result = TranslationQualityGuard.Check("Hi", "你好", "en", "zh");
        Assert.True(result.IsAcceptable);
    }
}

public sealed class TranslationChatClientQualityGuardIntegrationTests
{
    private static readonly ModelProfile LocalProfile =
        new StaticModelCatalog().FindById(ModelRegistry.Gemma4_12B.Id)!;

    private static readonly ModelProfile CloudProfile = new ModelProfile(
        "gpt-4o-mini", "Cloud GPT-4o-mini",
        ModelTaskType.Translation,
        ModelProviderKind.OpenAICompatible,
        ModelRuntimeKind.RemoteHttp,
        ModelExecutionKind.ChatCompletions,
        [],
        new ModelDescriptor("gpt-4o-mini", "GPT-4o-mini", "", 0, ModelType.Translation),
        SupportsAllLanguages: true);

    [Fact]
    public async Task GetResponseAsync_EscalatesToCloud_WhenQualityGuardFails()
    {
        var selector = NSubstitute.Substitute.For<IModelSelector>();
        var invocationService = NSubstitute.Substitute.For<IModelInvocationService>();

        // Local returns a bad (suspiciously short) translation
        selector.SelectTranslationProfile("en", "fr", Arg.Is<TranslationRoutingContext?>(c => c == null || !c.IsHighQualityMode))
            .Returns(LocalProfile);
        // Cloud escalation path
        selector.SelectTranslationProfile("en", "fr", Arg.Is<TranslationRoutingContext?>(c => c != null && c.IsHighQualityMode))
            .Returns(CloudProfile);

        // Local model returns just "ok" for a 50+ char source → quality guard fires
        var source = new string('a', 50);
        invocationService
            .InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.RuntimeKind == ModelRuntimeKind.LlamaServer),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("ok"));

        // Cloud model returns a proper translation
        invocationService
            .InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.RuntimeKind == ModelRuntimeKind.RemoteHttp),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("Good cloud translation output here."));

        var client = new TranslationChatClient(selector, invocationService);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["sourceLang"] = "en",
                ["targetLang"] = "fr",
                ["taskType"] = "Translation",
                ["textLength"] = source.Length,
                ["sourceText"] = source,
                ["sourceLangName"] = "English",
                ["targetLangName"] = "French"
            }
        };

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, source)],
            options, CancellationToken.None);

        Assert.Equal("Good cloud translation output here.", response.Text);
        Assert.Equal("gpt-4o-mini", response.ModelId); // cloud model used
    }
}
