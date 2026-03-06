using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LiveLingo.Core.Tests.Translation;

public class TranslationPipelineTests
{
    private readonly ILanguageDetector _detector;
    private readonly ITranslationEngine _engine;
    private readonly ILogger<TranslationPipeline> _logger;
    private readonly TranslationPipeline _pipeline;

    public TranslationPipelineTests()
    {
        _detector = Substitute.For<ILanguageDetector>();
        _engine = Substitute.For<ITranslationEngine>();
        _logger = Substitute.For<ILogger<TranslationPipeline>>();
        _pipeline = new TranslationPipeline(_detector, _engine, [], _logger);
    }

    [Fact]
    public async Task ProcessAsync_DetectsLanguage_WhenSourceLanguageNull()
    {
        _detector.DetectAsync("你好", Arg.Any<CancellationToken>())
            .Returns(new DetectionResult("zh", 0.99f));
        _engine.TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Hello");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("你好", null, "en", null), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal("zh", result.DetectedSourceLanguage);
        await _detector.Received(1).DetectAsync("你好", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_SkipsDetection_WhenSourceLanguageProvided()
    {
        _engine.TranslateAsync("你好", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Hello");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("你好", "zh", "en", null), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal("zh", result.DetectedSourceLanguage);
        await _detector.DidNotReceive().DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSourceText_WhenSameLanguage()
    {
        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("Hello", "en", "en", null), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal("Hello", result.RawTranslation);
        Assert.Equal(TimeSpan.Zero, result.TranslationDuration);
        await _engine.DidNotReceive()
            .TranslateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_DetectsAndReturnsSourceText_WhenSameLanguageDetected()
    {
        _detector.DetectAsync("Hello", Arg.Any<CancellationToken>())
            .Returns(new DetectionResult("en", 0.95f));

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("Hello", null, "en", null), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal("en", result.DetectedSourceLanguage);
    }

    [Fact]
    public async Task ProcessAsync_MeasuresTranslationDuration()
    {
        _engine.TranslateAsync("Test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(50);
                return "Translated";
            });

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("Test", "zh", "en", null), CancellationToken.None);

        Assert.True(result.TranslationDuration.TotalMilliseconds >= 40);
        Assert.Null(result.PostProcessingDuration);
    }

    [Fact]
    public async Task ProcessAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        _detector.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DetectionResult("zh", 0.9f));
        _engine.TranslateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.ArgAt<CancellationToken>(3);
                await Task.Delay(5000, ct);
                return "result";
            });

        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _pipeline.ProcessAsync(
                new TranslationRequest("Test", null, "en", null), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_AppliesPostProcessors()
    {
        var summarizer = Substitute.For<ITextProcessor>();
        summarizer.Name.Returns("summarize");
        summarizer.ProcessAsync("Hello world", "en", Arg.Any<CancellationToken>())
            .Returns("Hello");

        _engine.TranslateAsync("你好世界", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Hello world");

        var pipeline = new TranslationPipeline(
            _detector, _engine, new[] { summarizer }, _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("你好世界", "zh", "en",
                new ProcessingOptions(Summarize: true)), CancellationToken.None);

        Assert.Equal("Hello", result.Text);
        Assert.Equal("Hello world", result.RawTranslation);
        Assert.NotNull(result.PostProcessingDuration);
    }

    [Fact]
    public async Task ProcessAsync_SkipsPostProcessors_WhenNoneConfigured()
    {
        _engine.TranslateAsync("Test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("Test", "zh", "en", null), CancellationToken.None);

        Assert.Equal("Translated", result.Text);
        Assert.Null(result.PostProcessingDuration);
    }

    [Fact]
    public async Task ProcessAsync_ChainsMultipleProcessors()
    {
        var optimizer = Substitute.For<ITextProcessor>();
        optimizer.Name.Returns("optimize");
        optimizer.ProcessAsync("raw", "en", Arg.Any<CancellationToken>())
            .Returns("optimized");

        var colloquializer = Substitute.For<ITextProcessor>();
        colloquializer.Name.Returns("colloquialize");
        colloquializer.ProcessAsync("optimized", "en", Arg.Any<CancellationToken>())
            .Returns("casual");

        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("raw");

        var pipeline = new TranslationPipeline(
            _detector, _engine, new[] { optimizer, colloquializer }, _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en",
                new ProcessingOptions(Optimize: true, Colloquialize: true)), CancellationToken.None);

        Assert.Equal("casual", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_IgnoresMissingProcessor()
    {
        _engine.TranslateAsync("test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("test", "zh", "en",
                new ProcessingOptions(Summarize: true)), CancellationToken.None);

        Assert.Equal("translated", result.Text);
    }
}
