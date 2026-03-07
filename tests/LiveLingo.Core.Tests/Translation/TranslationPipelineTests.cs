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

    [Fact]
    public async Task ProcessAsync_IgnoresUnmatchedProcessor_WhenOtherProcessorsExist()
    {
        var optimizer = Substitute.For<ITextProcessor>();
        optimizer.Name.Returns("optimize");

        _engine.TranslateAsync("x", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var pipeline = new TranslationPipeline(
            _detector, _engine, new[] { optimizer }, _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("x", "zh", "en",
                new ProcessingOptions(Summarize: true)), CancellationToken.None);

        Assert.Equal("translated", result.Text);
        Assert.NotNull(result.PostProcessingDuration);
    }

    [Fact]
    public async Task ProcessAsync_CancellationBetweenDetectionAndTranslation()
    {
        using var cts = new CancellationTokenSource();
        _detector.DetectAsync("test", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cts.Cancel();
                return new DetectionResult("zh", 0.9f);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _pipeline.ProcessAsync(
                new TranslationRequest("test", null, "en", null), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringPostProcessing()
    {
        using var cts = new CancellationTokenSource();
        var processor = Substitute.For<ITextProcessor>();
        processor.Name.Returns("summarize");
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cts.Cancel();
                callInfo.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
                return "result";
            });

        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var pipeline = new TranslationPipeline(
            _detector, _engine, new[] { processor }, _logger);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pipeline.ProcessAsync(
                new TranslationRequest("src", "zh", "en",
                    new ProcessingOptions(Summarize: true)), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_PostProcessingDuration_IsSeparateFromTranslation()
    {
        var processor = Substitute.For<ITextProcessor>();
        processor.Name.Returns("optimize");
        processor.ProcessAsync("translated", "en", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return "optimized";
            });

        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return "translated";
            });

        var pipeline = new TranslationPipeline(
            _detector, _engine, new[] { processor }, _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en",
                new ProcessingOptions(Optimize: true)), CancellationToken.None);

        Assert.NotNull(result.PostProcessingDuration);
        Assert.True(result.PostProcessingDuration!.Value.TotalMilliseconds >= 40);
        Assert.True(result.TranslationDuration.TotalMilliseconds >= 40);
    }

    [Fact]
    public async Task ProcessAsync_DetectedLanguage_IsLoggedAndReturned()
    {
        _detector.DetectAsync("Test", Arg.Any<CancellationToken>())
            .Returns(new DetectionResult("zh", 0.95f));
        _engine.TranslateAsync("Test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("Test", null, "en", null), CancellationToken.None);

        Assert.Equal("zh", result.DetectedSourceLanguage);
        Assert.Equal("Translated", result.RawTranslation);
        _logger.Received().Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ProcessAsync_OptimizeWithNoProcessor_DoesNotThrow()
    {
        _engine.TranslateAsync("test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("test", "zh", "en",
                new ProcessingOptions(Optimize: true)), CancellationToken.None);

        Assert.Equal("translated", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_ColloquializeWithNoProcessor_DoesNotThrow()
    {
        _engine.TranslateAsync("test", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("test", "zh", "en",
                new ProcessingOptions(Colloquialize: true)), CancellationToken.None);

        Assert.Equal("translated", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_CancellationAfterTranslation_BeforePostProcessing()
    {
        using var cts = new CancellationTokenSource();
        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                cts.Cancel();
                return "translated";
            });

        var processor = Substitute.For<ITextProcessor>();
        processor.Name.Returns("summarize");

        var pipeline = new TranslationPipeline(
            _detector, _engine, [processor], _logger);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pipeline.ProcessAsync(
                new TranslationRequest("src", "zh", "en",
                    new ProcessingOptions(Summarize: true)), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_CancellationAfterTranslation_WithoutPostProcessing()
    {
        using var cts = new CancellationTokenSource();
        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return "translated";
            });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _pipeline.ProcessAsync(
                new TranslationRequest("src", "zh", "en", null), cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_PostProcessDuration_ExcludesTranslationTime()
    {
        var processor = Substitute.For<ITextProcessor>();
        processor.Name.Returns("optimize");
        processor.ProcessAsync("translated", "en", Arg.Any<CancellationToken>())
            .Returns("optimized");

        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(200);
                return "translated";
            });

        var pipeline = new TranslationPipeline(
            _detector, _engine, [processor], _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en",
                new ProcessingOptions(Optimize: true)), CancellationToken.None);

        Assert.NotNull(result.PostProcessingDuration);
        Assert.True(result.TranslationDuration.TotalMilliseconds >= 150);
        Assert.True(result.PostProcessingDuration!.Value.TotalMilliseconds < 150,
            $"Post-processing took {result.PostProcessingDuration.Value.TotalMilliseconds}ms, expected < 150ms");
    }
}
