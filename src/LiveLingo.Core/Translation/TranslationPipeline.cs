using System.Diagnostics;
using LiveLingo.Core.Engines;
using LiveLingo.Core.LanguageDetection;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Translation;

public sealed class TranslationPipeline : ITranslationPipeline
{
    private readonly ILanguageDetector _detector;
    private readonly ITranslationEngine _engine;
    private readonly IModelReadinessService _modelReadiness;
    private readonly IEnumerable<ITextProcessor> _processors;
    private readonly ILogger<TranslationPipeline> _logger;

    public TranslationPipeline(
        ILanguageDetector detector,
        ITranslationEngine engine,
        IModelReadinessService modelReadiness,
        IEnumerable<ITextProcessor> processors,
        ILogger<TranslationPipeline> logger)
    {
        _detector = detector;
        _engine = engine;
        _modelReadiness = modelReadiness;
        _processors = processors;
        _logger = logger;
    }

    public async Task<TranslationResult> ProcessAsync(
        TranslationRequest request, CancellationToken ct)
    {
        var srcLang = request.SourceLanguage;
        if (string.IsNullOrEmpty(srcLang))
        {
            var detection = await _detector.DetectAsync(request.SourceText, ct);
            srcLang = detection.Language;
            _logger.LogDebug("Detected language: {Lang} ({Conf:P0})",
                detection.Language, detection.Confidence);
        }

        if (srcLang == request.TargetLanguage)
        {
            return new TranslationResult(
                request.SourceText, srcLang, request.SourceText,
                TimeSpan.Zero, null);
        }

        ct.ThrowIfCancellationRequested();

        _modelReadiness.EnsureTranslationModelReady(srcLang, request.TargetLanguage);
        if (request.PostProcessing is not null)
            _modelReadiness.EnsurePostProcessingModelReady();

        var sw = Stopwatch.StartNew();
        string translated;
        try
        {
            translated = await _engine.TranslateAsync(
                request.SourceText, srcLang, request.TargetLanguage, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (ModelNotReadyException) { throw; }
        catch (NotSupportedException) { throw; }
        catch (TranslationFailedException) { throw; }
        catch (InvalidOperationException ex)
        {
            throw new TranslationFailedException("Translation failed.", ex);
        }
        var translationDuration = sw.Elapsed;

        ct.ThrowIfCancellationRequested();

        var finalText = translated;
        TimeSpan? postDuration = null;

        if (request.PostProcessing is { } opts)
        {
            sw.Restart();
            foreach (var proc in SelectProcessors(opts))
            {
                ct.ThrowIfCancellationRequested();
                finalText = await proc.ProcessAsync(finalText, request.TargetLanguage, ct);
            }
            postDuration = sw.Elapsed;
        }

        return new TranslationResult(
            finalText, srcLang, translated,
            translationDuration, postDuration);
    }

    private IEnumerable<ITextProcessor> SelectProcessors(ProcessingOptions opts)
    {
        if (opts.Summarize)
        {
            var p = _processors.FirstOrDefault(p => p.Name == "summarize");
            if (p is not null) yield return p;
        }
        if (opts.Optimize)
        {
            var p = _processors.FirstOrDefault(p => p.Name == "optimize");
            if (p is not null) yield return p;
        }
        if (opts.Colloquialize)
        {
            var p = _processors.FirstOrDefault(p => p.Name == "colloquialize");
            if (p is not null) yield return p;
        }
    }
}
