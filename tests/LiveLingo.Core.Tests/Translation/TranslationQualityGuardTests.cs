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

    [Fact]
    public void Check_SentenceCountDrops_FailsForMultiSentenceSource()
    {
        // Regression: model outputs "Hello, coward." for 2-sentence source.
        var src = "你好啊，胆小鬼。 你是不是不知道我是谁？"; // 2 sentences
        var tgt = "Hello, coward.";                       // 1 sentence

        var result = TranslationQualityGuard.Check(src, tgt, "zh", "en");

        Assert.False(result.IsAcceptable);
        Assert.Contains("Sentence count", result.FailureReason);
    }

    [Fact]
    public void Check_SentenceCountMatches_Passes()
    {
        var src = "你好啊，胆小鬼。 你是不是不知道我是谁？";
        var tgt = "Hello, coward. Don't you know who I am?";

        var result = TranslationQualityGuard.Check(src, tgt, "zh", "en");

        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Check_MergedSentences_FailsWhenTargetDrops()
    {
        // 3 source sentences merged into 2 target sentences must fail: the
        // pipeline now translates one sentence at a time, so any drop points
        // at an upstream caller bypassing segmentation.
        var src = "Hi there. How are you? Nice day.";
        var tgt = "Hi there, how are you? Nice day.";

        var result = TranslationQualityGuard.Check(src, tgt, "en", "zh");

        Assert.False(result.IsAcceptable);
        Assert.Contains("Sentence count", result.FailureReason);
    }

    // --- Failure reason content (string mutation guard) ---

    [Fact]
    public void Check_EmptyTranslation_ReasonDescribesEmptyOutput()
    {
        var result = TranslationQualityGuard.Check("Hello world", "", "en", "zh");
        Assert.False(result.IsAcceptable);
        Assert.Equal("Empty translation output.", result.FailureReason);
    }

    [Fact]
    public void Check_NumericFidelity_MissingNumber_ReasonNamesTheNumber()
    {
        var result = TranslationQualityGuard.Check("Year 2025", "anno dominum", "en", "la");
        Assert.False(result.IsAcceptable);
        Assert.Contains("'2025'", result.FailureReason);
        Assert.Contains("not found", result.FailureReason);
    }

    // --- LongSourceThreshold / MinOutputCharsForLongSource boundaries ---

    [Fact]
    public void Check_SourceExactlyLongSourceThreshold_TinyOutput_Fails()
    {
        // Source length == 40 (threshold). Any output shorter than 10 chars
        // must trigger the omission guard.
        var source = new string('a', 40);
        var result = TranslationQualityGuard.Check(source, "short", "en", "fr");

        Assert.False(result.IsAcceptable);
        Assert.Contains("too short", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_SourceShorterThanThreshold_EscapesOmissionGuard()
    {
        // Source length == 39 (threshold − 1). Even a 1-char output passes
        // the omission guard (the ratio guard may still fire, below).
        var source = new string('a', 39);
        var result = TranslationQualityGuard.Check(source, "abcdefg", "en", "fr");

        Assert.True(result.IsAcceptable,
            $"Expected pass but got: {result.FailureReason}");
    }

    [Fact]
    public void Check_LongSource_OutputExactlyMinChars_Passes()
    {
        // translation.Length == 10 (MinOutputCharsForLongSource). The guard
        // condition is `< 10`, so exactly 10 must pass.
        var source = new string('a', 100);
        var translation = new string('b', 10);
        var result = TranslationQualityGuard.Check(source, translation, "en", "fr");

        Assert.True(result.IsAcceptable,
            $"Expected pass but got: {result.FailureReason}");
    }

    // --- Ratio guards (non-CJK pair only) ---

    [Fact]
    public void Check_NonCjkPair_RatioBelowMin_Fails()
    {
        // Translation 50 chars is long enough to escape the omission guard
        // (>= 10 chars) but produces ratio 0.05 < 0.10 minimum.
        var source = new string('a', 1000);
        var translation = new string('b', 50);
        var result = TranslationQualityGuard.Check(source, translation, "en", "fr");

        Assert.False(result.IsAcceptable);
        Assert.Contains("Length ratio", result.FailureReason);
        Assert.Contains("below minimum", result.FailureReason);
    }

    [Fact]
    public void Check_NonCjkPair_RatioAboveMax_Fails()
    {
        // Source 10 chars keeps us under the LongSourceThreshold (skips
        // omission guard). Translation 150 chars → ratio 15 > 10 maximum.
        var source = new string('a', 10);
        var translation = new string('b', 150);
        var result = TranslationQualityGuard.Check(source, translation, "en", "fr");

        Assert.False(result.IsAcceptable);
        Assert.Contains("above maximum", result.FailureReason);
    }

    [Fact]
    public void Check_NonCjkPair_RatioExactlyAtMax_Passes()
    {
        // Ratio = 100/10 = 10.0 which is the MaxRatio. The guard condition
        // is `>` MaxRatio – exactly at the boundary must pass.
        var source = new string('a', 10);
        var translation = new string('b', 100);
        var result = TranslationQualityGuard.Check(source, translation, "en", "fr");

        Assert.True(result.IsAcceptable,
            $"Expected pass at ratio == MaxRatio but got: {result.FailureReason}");
    }

    [Theory]
    [InlineData("zh", "en")]
    [InlineData("ja", "en")]
    [InlineData("ko", "en")]
    [InlineData("en", "zh")]
    [InlineData("en", "ja")]
    [InlineData("en", "ko")]
    public void Check_CompressiveLanguagePair_SkipsRatioGuard(string src, string tgt)
    {
        // A ratio-violating translation is accepted because the pair involves
        // a CJK language on either side (character density is not comparable).
        // Translation 50 chars is long enough to escape the omission guard so
        // the test isolates the ratio-skip behaviour.
        var source = new string('a', 1000);
        var translation = new string('b', 50);
        var result = TranslationQualityGuard.Check(source, translation, src, tgt);

        Assert.True(result.IsAcceptable,
            $"Expected pass for {src}→{tgt} but got: {result.FailureReason}");
    }

    [Theory]
    [InlineData("en", "fr")]
    [InlineData("es", "de")]
    public void Check_NonCompressivePair_AppliesRatioGuard(string src, string tgt)
    {
        // Non-CJK pair with extreme ratio must fail (proves the guard runs).
        var source = new string('a', 1000);
        var translation = new string('b', 50);
        var result = TranslationQualityGuard.Check(source, translation, src, tgt);

        Assert.False(result.IsAcceptable);
    }

    // --- Pathological repetition boundaries ---

    [Fact]
    public void Check_ExactlyEleven_RepeatedWords_Passes()
    {
        // 11 words total (below the 12-word minimum for repetition check).
        // Paired with a short source that keeps the ratio guard satisfied.
        var repetitive = string.Join(" ", Enumerable.Repeat("the", 11));
        var source = new string('a', 10);
        var result = TranslationQualityGuard.Check(source, repetitive, "en", "fr");

        Assert.True(result.IsAcceptable,
            $"Expected pass but got: {result.FailureReason}");
    }

    [Fact]
    public void Check_ExactlyTwelve_IdenticalWords_Fails()
    {
        // 12 consecutive identical words – the >= 12 threshold trips the guard.
        var repetitive = string.Join(" ", Enumerable.Repeat("the", 12));
        var source = new string('a', 10);
        var result = TranslationQualityGuard.Check(source, repetitive, "en", "fr");

        Assert.False(result.IsAcceptable);
        Assert.Contains("repetition", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_RunBrokenByDifferentWord_ResetsCounter()
    {
        // "the the the X the the the the the the the the the" – 10 repeats
        // after the interrupter. Max run is 10 (< 12) → passes.
        var words = new List<string>();
        words.AddRange(Enumerable.Repeat("the", 3));
        words.Add("break");
        words.AddRange(Enumerable.Repeat("the", 10));
        var translation = string.Join(" ", words);
        var source = new string('a', 30);

        var result = TranslationQualityGuard.Check(source, translation, "en", "fr");
        Assert.True(result.IsAcceptable,
            $"Expected pass but got: {result.FailureReason}");
    }
}

public sealed class TranslationChatClientQualityGuardIntegrationTests
{
    private static readonly ModelProfile LocalProfile =
        new StaticModelCatalog().FindById(ModelRegistry.Gemma4_26B_A4B.Id)!;

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
    public async Task GetResponseAsync_FallsBackToCloudCandidate_WhenQualityGuardFailsOnLocal()
    {
        var selector = NSubstitute.Substitute.For<IModelSelector>();
        var invocationService = NSubstitute.Substitute.For<IModelInvocationService>();

        // Plan: local candidate first, cloud candidate as fallback.
        var plan = new TranslationRoutePlan(
        [
            new TranslationRouteCandidate(LocalProfile, TranslationRouteTier.Local, TimeSpan.FromSeconds(8)),
            new TranslationRouteCandidate(CloudProfile, TranslationRouteTier.Cloud, TimeSpan.FromSeconds(4))
        ]);
        selector.BuildTranslationRoutePlan("en", "fr", Arg.Any<TranslationRoutingContext?>()).Returns(plan);

        var source = new string('a', 50);
        // Local returns a bad (suspiciously short) translation → quality guard rejects it.
        invocationService
            .InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.RuntimeKind == ModelRuntimeKind.LlamaServer),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("ok"));

        // Cloud fallback returns a proper translation that passes the guard.
        invocationService
            .InvokeAsync(
                Arg.Is<ModelInvocationRequest>(r => r.Profile.RuntimeKind == ModelRuntimeKind.RemoteHttp),
                Arg.Any<CancellationToken>())
            .Returns(new ModelInvocationResult("Good cloud translation output here."));

        var invoker = new FallbackTranslationInvoker(invocationService, new InProcessTranslationTelemetry());
        var client = new TranslationChatClient(selector, invocationService, invoker);
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
        Assert.Equal("gpt-4o-mini", response.ModelId);
    }
}
