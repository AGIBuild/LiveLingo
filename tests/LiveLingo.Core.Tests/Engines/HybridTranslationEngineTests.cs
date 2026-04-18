using System.Runtime.CompilerServices;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LiveLingo.Core.Tests.Engines;

public class HybridTranslationEngineTests
{
    private readonly IFastPathTranslationEngine _fastPath = Substitute.For<IFastPathTranslationEngine>();
    private readonly IChatPathTranslationEngine _chatPath = Substitute.For<IChatPathTranslationEngine>();
    private readonly IModelCatalog _catalog = Substitute.For<IModelCatalog>();
    private readonly IModelManager _modelManager = Substitute.For<IModelManager>();

    public HybridTranslationEngineTests()
    {
        _fastPath.SupportedLanguages.Returns(
            new List<LanguageInfo> { new("zh", "zh"), new("en", "en") });
        _chatPath.SupportedLanguages.Returns(
            new List<LanguageInfo> { new("en", "English"), new("fr", "Français") });
    }

    private HybridTranslationEngine CreateEngine() =>
        new(_fastPath, _chatPath, _catalog, _modelManager, NullLogger<HybridTranslationEngine>.Instance);

    // --- SupportedLanguages aggregation ---

    [Fact]
    public void SupportedLanguages_MergesBothEngines_Deduplicated()
    {
        var engine = CreateEngine();

        Assert.Equal(3, engine.SupportedLanguages.Count);
        Assert.Contains(engine.SupportedLanguages, l => l.Code == "zh");
        Assert.Contains(engine.SupportedLanguages, l => l.Code == "en");
        Assert.Contains(engine.SupportedLanguages, l => l.Code == "fr");
    }

    // --- SupportsLanguagePair ---

    [Fact]
    public void SupportsLanguagePair_TrueWhenFastPathSupports()
    {
        _fastPath.SupportsLanguagePair("zh", "en").Returns(true);
        _chatPath.SupportsLanguagePair("zh", "en").Returns(false);

        var engine = CreateEngine();
        Assert.True(engine.SupportsLanguagePair("zh", "en"));
    }

    [Fact]
    public void SupportsLanguagePair_TrueWhenOnlyChatPathSupports()
    {
        _fastPath.SupportsLanguagePair("en", "fr").Returns(false);
        _chatPath.SupportsLanguagePair("en", "fr").Returns(true);

        var engine = CreateEngine();
        Assert.True(engine.SupportsLanguagePair("en", "fr"));
    }

    [Fact]
    public void SupportsLanguagePair_FalseWhenNeitherSupports()
    {
        var engine = CreateEngine();
        Assert.False(engine.SupportsLanguagePair("xx", "yy"));
    }

    // --- Routing: fast path eligible ---

    [Fact]
    public async Task TranslateAsync_UsesFastPath_WhenEligibleAndAssetsPresent()
    {
        ConfigureFastPathEligible("zh", "en", assetsReady: true);
        _fastPath.TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("hello");

        var engine = CreateEngine();
        var result = await engine.TranslateAsync("你好", "zh", "en", CancellationToken.None);

        Assert.Equal("hello", result);
        await _fastPath.Received(1).TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>());
        await _chatPath.DidNotReceive().TranslateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Routing: fast path supported but assets missing ---

    [Fact]
    public async Task TranslateAsync_FallsToChatPath_WhenFastPathAssetsMissing()
    {
        ConfigureFastPathEligible("zh", "en", assetsReady: false);
        _chatPath.TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("hello (chat)");

        var engine = CreateEngine();
        var result = await engine.TranslateAsync("你好", "zh", "en", CancellationToken.None);

        Assert.Equal("hello (chat)", result);
        await _chatPath.Received(1).TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>());
        await _fastPath.DidNotReceive().TranslateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Routing: fast path doesn't support pair ---

    [Fact]
    public async Task TranslateAsync_UsesChatPath_WhenFastPathDoesNotSupport()
    {
        _fastPath.SupportsLanguagePair("en", "fr").Returns(false);
        _chatPath.TranslateAsync("hello", "en", "fr", Arg.Any<CancellationToken>())
            .Returns("bonjour");

        var engine = CreateEngine();
        var result = await engine.TranslateAsync("hello", "en", "fr", CancellationToken.None);

        Assert.Equal("bonjour", result);
        await _chatPath.Received(1).TranslateAsync("hello", "en", "fr", Arg.Any<CancellationToken>());
    }

    // --- Streaming: fast path ---

    [Fact]
    public async Task TranslateStreamingAsync_UsesFastPath_WhenEligible()
    {
        ConfigureFastPathEligible("zh", "en", assetsReady: true);
        _fastPath.TranslateStreamingAsync("你好", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(SingleDeltaAsync("hello"));

        var engine = CreateEngine();
        var results = new List<TranslationDelta>();
        await foreach (var delta in engine.TranslateStreamingAsync("你好", "zh", "en"))
            results.Add(delta);

        Assert.Single(results);
        Assert.Equal("hello", results[0].Text);
        _ = _fastPath.Received(1).TranslateStreamingAsync(
            "你好", "zh", "en", Arg.Any<CancellationToken>());
    }

    // --- Streaming: chat path ---

    [Fact]
    public async Task TranslateStreamingAsync_UsesChatPath_WhenFastPathIneligible()
    {
        _fastPath.SupportsLanguagePair("en", "fr").Returns(false);
        _chatPath.TranslateStreamingAsync("hi", "en", "fr", Arg.Any<CancellationToken>())
            .Returns(SingleDeltaAsync("salut"));

        var engine = CreateEngine();
        var results = new List<TranslationDelta>();
        await foreach (var delta in engine.TranslateStreamingAsync("hi", "en", "fr"))
            results.Add(delta);

        Assert.Single(results);
        Assert.Equal("salut", results[0].Text);
        _ = _chatPath.Received(1).TranslateStreamingAsync(
            "hi", "en", "fr", Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private void ConfigureFastPathEligible(string src, string tgt, bool assetsReady)
    {
        _fastPath.SupportsLanguagePair(src, tgt).Returns(true);

        var descriptor = new ModelDescriptor(
            $"opus-mt-{src}-{tgt}",
            $"MarianMT {src}→{tgt}",
            "https://example/model.onnx",
            1000,
            ModelType.Translation);
        var profile = new ModelProfile(
            descriptor.Id,
            descriptor.DisplayName,
            ModelTaskType.Translation,
            ModelProviderKind.MarianOnnx,
            ModelRuntimeKind.OnnxRuntime,
            ModelExecutionKind.OnnxTranslation,
            [src, tgt],
            descriptor);

        _catalog.GetProfiles(ModelTaskType.Translation).Returns(new[] { profile });
        _modelManager.HasAllExpectedLocalAssets(descriptor).Returns(assetsReady);
    }

    private static async IAsyncEnumerable<TranslationDelta> SingleDeltaAsync(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new TranslationDelta(text);
    }
}
