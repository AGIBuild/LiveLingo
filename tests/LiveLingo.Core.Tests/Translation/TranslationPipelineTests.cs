using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LiveLingo.Core.Tests.Translation;

public class TranslationPipelineTests
{
    private readonly ILanguageDetector _detector;
    private readonly ITranslationEngine _engine;
    private readonly IModelReadinessService _readiness;
    private readonly ILogger<TranslationPipeline> _logger;
    private readonly TranslationPipeline _pipeline;

    public TranslationPipelineTests()
    {
        _detector = Substitute.For<ILanguageDetector>();
        _engine = Substitute.For<ITranslationEngine>();
        _readiness = Substitute.For<IModelReadinessService>();
        _logger = Substitute.For<ILogger<TranslationPipeline>>();
        _pipeline = new TranslationPipeline(_detector, _engine, _readiness, [], _logger);
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
            _detector, _engine, _readiness, new[] { summarizer }, _logger);

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
            _detector, _engine, _readiness, new[] { optimizer, colloquializer }, _logger);

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
            _detector, _engine, _readiness, new[] { optimizer }, _logger);

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
            _detector, _engine, _readiness, new[] { processor }, _logger);

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
            _detector, _engine, _readiness, new[] { processor }, _logger);

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
            _detector, _engine, _readiness, [processor], _logger);

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
            _detector, _engine, _readiness, [processor], _logger);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en",
                new ProcessingOptions(Optimize: true)), CancellationToken.None);

        Assert.NotNull(result.PostProcessingDuration);
        Assert.True(result.TranslationDuration.TotalMilliseconds >= 150);
        Assert.True(result.PostProcessingDuration!.Value.TotalMilliseconds < 150,
            $"Post-processing took {result.PostProcessingDuration.Value.TotalMilliseconds}ms, expected < 150ms");
    }

    [Fact]
    public async Task ProcessAsync_ThrowsModelNotReady_WhenPostProcessingModelMissing()
    {
        _readiness
            .When(r => r.EnsurePostProcessingModelReady())
            .Do(_ => throw new ModelNotReadyException(
                ModelType.PostProcessing,
                ModelRegistry.Qwen25_15B.Id,
                "missing",
                "download"));

        await Assert.ThrowsAsync<ModelNotReadyException>(() => _pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en", new ProcessingOptions(Summarize: true)),
            CancellationToken.None));

        await _engine.DidNotReceive()
            .TranslateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_DoesNotCheckPostProcessingReadiness_WhenModeOff()
    {
        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("translated");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en", null),
            CancellationToken.None);

        Assert.Equal("translated", result.Text);
        _readiness.DidNotReceive().EnsurePostProcessingModelReady();
    }

    [Fact]
    public async Task ProcessAsync_WrapsEngineFailure_AsTranslationFailedException()
    {
        _engine.TranslateAsync("src", "zh", "en", Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("Translation returned empty output."));

        var ex = await Assert.ThrowsAsync<TranslationFailedException>(() => _pipeline.ProcessAsync(
            new TranslationRequest("src", "zh", "en", null),
            CancellationToken.None));

        Assert.Equal("Translation failed.", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    // --- Multi-sentence segmentation (regression) ---

    [Fact]
    public async Task ProcessAsync_MultiSentenceShortText_TranslatesEachSentenceIndependently()
    {
        // Regression: "你好啊，胆小鬼。 你是不是不知道我是谁？" used to be one
        // prompt to the engine, allowing the model to drop the second clause.
        // Pipeline now segments per sentence and joins the results.
        const string source = "你好啊，胆小鬼。 你是不是不知道我是谁？";

        _engine.TranslateAsync("你好啊，胆小鬼。", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Hello, coward.");
        _engine.TranslateAsync("你是不是不知道我是谁？", "zh", "en", Arg.Any<CancellationToken>())
            .Returns("Don't you know who I am?");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "zh", "en", null), CancellationToken.None);

        Assert.Equal("Hello, coward. Don't you know who I am?", result.Text);
        await _engine.Received(1).TranslateAsync("你好啊，胆小鬼。", "zh", "en", Arg.Any<CancellationToken>());
        await _engine.Received(1).TranslateAsync("你是不是不知道我是谁？", "zh", "en", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MultiSentenceAcrossParagraphs_JoinsWithDoubleNewline()
    {
        const string source = "First sentence.\n\nSecond sentence.";

        _engine.TranslateAsync("First sentence.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("第一句。");
        _engine.TranslateAsync("Second sentence.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("第二句。");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("第一句。\n\n第二句。", result.Text);
    }

    [Fact]
    public async Task ProcessAsync_MultiSentenceCjkTarget_JoinsWithoutExtraSpace()
    {
        // CJK targets already carry a full-width gap after their sentence-end
        // punctuation, so the segment re-assembler must not inject a space.
        const string source = "Hi. Bye.";

        _engine.TranslateAsync("Hi.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("你好。");
        _engine.TranslateAsync("Bye.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("再见。");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("你好。再见。", result.Text);
    }

    // --- Single-newline preservation (user-authored hard wraps) ---

    [Fact]
    public async Task ProcessAsync_SingleNewlineBetweenLines_PreservesNewlineInOutput()
    {
        // Two line-connected segments are merged into one translation unit so
        // the engine sees the full semantic context. The unit output is split
        // back on '\n' to recover per-line fragments before reassembly.
        const string source = "first line\nsecond line";

        _engine.TranslateAsync("first line\nsecond line", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("第一行\n第二行");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("第一行\n第二行", result.Text);
        await _engine.Received(1).TranslateAsync(
            "first line\nsecond line", "en", "zh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_MixedLineAndParagraphBreaks_PreservesBoth()
    {
        // "alpha\nbeta\n\ngamma" groups alpha+beta into a single unit
        // (Line break, neither ends with a sentence mark) and keeps gamma
        // as its own unit across the paragraph break.
        const string source = "alpha\nbeta\n\ngamma";

        _engine.TranslateAsync("alpha\nbeta", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("甲\n乙");
        _engine.TranslateAsync("gamma", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("丙");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("甲\n乙\n\n丙", result.Text);
        await _engine.Received(1).TranslateAsync("alpha\nbeta", "en", "zh", Arg.Any<CancellationToken>());
        await _engine.Received(1).TranslateAsync("gamma", "en", "zh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_LineTerminatedBySentenceMark_StaysIndependent()
    {
        // "Hello.\nWorld." – the first line ends with '.', so the planner
        // must NOT merge it with the next line. Each line is its own unit
        // and its own engine call.
        const string source = "Hello.\nWorld.";

        _engine.TranslateAsync("Hello.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("你好。");
        _engine.TranslateAsync("World.", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("世界。");

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("你好。\n世界。", result.Text);
        await _engine.DidNotReceive().TranslateAsync(
            "Hello.\nWorld.", "en", "zh", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_UnitOutputMissingNewlines_FallsBackWithoutLosingContent()
    {
        // When a small local LLM rewrites "alpha\nbeta" as one line, we must
        // not silently drop the second fragment. Content is attributed to
        // the first fragment; the Line separator still renders so the user
        // sees the translation (with a trailing newline, trimmed by the UI
        // layer) instead of an empty second line.
        const string source = "alpha\nbeta";

        _engine.TranslateAsync("alpha\nbeta", "en", "zh", Arg.Any<CancellationToken>())
            .Returns("甲乙");  // LLM merged the two lines

        var result = await _pipeline.ProcessAsync(
            new TranslationRequest(source, "en", "zh", null), CancellationToken.None);

        Assert.Equal("甲乙\n", result.Text);
    }
}
