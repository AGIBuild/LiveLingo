using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Tests.Integration;

public class TranslationRoutingIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task TranslationOnlyPath_DoesNotInvokePostProcessors()
    {
        var engine = new RecordingTranslationEngine();
        var summarize = new RecordingProcessor("summarize", text => $"sum:{text}");
        var optimize = new RecordingProcessor("optimize", text => $"opt:{text}");
        var pipeline = new TranslationPipeline(
            new ScriptBasedDetector(),
            engine,
            [summarize, optimize],
            NullLogger<TranslationPipeline>.Instance);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("你好", null, "en", null),
            CancellationToken.None);

        Assert.Equal("[zh->en] 你好", result.Text);
        Assert.Equal("zh", result.DetectedSourceLanguage);
        Assert.Equal(1, engine.CallCount);
        Assert.Equal(0, summarize.CallCount);
        Assert.Equal(0, optimize.CallCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostProcessingPath_InvokesMatchingProcessorOnly()
    {
        var engine = new RecordingTranslationEngine();
        var summarize = new RecordingProcessor("summarize", text => $"sum:{text}");
        var optimize = new RecordingProcessor("optimize", text => $"opt:{text}");
        var pipeline = new TranslationPipeline(
            new ScriptBasedDetector(),
            engine,
            [summarize, optimize],
            NullLogger<TranslationPipeline>.Instance);

        var result = await pipeline.ProcessAsync(
            new TranslationRequest("你好", null, "en", new ProcessingOptions(Optimize: true)),
            CancellationToken.None);

        Assert.Equal("opt:[zh->en] 你好", result.Text);
        Assert.Equal("[zh->en] 你好", result.RawTranslation);
        Assert.Equal(1, engine.CallCount);
        Assert.Equal(0, summarize.CallCount);
        Assert.Equal(1, optimize.CallCount);
        Assert.NotNull(result.PostProcessingDuration);
    }

    private sealed class RecordingTranslationEngine : ITranslationEngine
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<LanguageInfo> SupportedLanguages { get; } =
        [
            new("zh", "Chinese"),
            new("en", "English"),
        ];

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult($"[{sourceLanguage}->{targetLanguage}] {text}");
        }

        public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage)
            => string.Equals(sourceLanguage, "zh", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(targetLanguage, "en", StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingProcessor(string name, Func<string, string> transform) : ITextProcessor
    {
        public string Name { get; } = name;
        public int CallCount { get; private set; }

        public Task<string> ProcessAsync(string text, string language, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(transform(text));
        }

        public void Dispose()
        {
        }
    }
}
