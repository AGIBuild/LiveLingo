using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
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
    private readonly TextSegmenter _segmenter;

    public TranslationPipeline(
        ILanguageDetector detector,
        ITranslationEngine engine,
        IModelReadinessService modelReadiness,
        IEnumerable<ITextProcessor> processors,
        ILogger<TranslationPipeline> logger,
        TextSegmenter? segmenter = null)
    {
        _detector = detector;
        _engine = engine;
        _modelReadiness = modelReadiness;
        _processors = processors;
        _logger = logger;
        _segmenter = segmenter ?? new TextSegmenter();
    }

    public async Task<TranslationResult> ProcessAsync(
        TranslationRequest request, CancellationToken ct)
    {
        var srcLang = request.SourceLanguage;
        if (string.IsNullOrEmpty(srcLang))
        {
            var detection = await _detector.DetectAsync(request.SourceText, ct);
            srcLang = detection.Language;

            if (detection.Confidence < 0.6f)
                _logger.LogWarning(
                    "Low-confidence language detection: {Lang} ({Conf:P0}) – result may be unreliable.",
                    detection.Language, detection.Confidence);
            else
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

        var segments = _segmenter.Segment(request.SourceText);
        var sw = Stopwatch.StartNew();
        string translated;
        try
        {
            translated = segments.Count <= 1
                ? await _engine.TranslateAsync(
                    request.SourceText, srcLang, request.TargetLanguage, ct)
                : await TranslateSegmentsAsync(segments, srcLang, request.TargetLanguage, ct);
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

    public async IAsyncEnumerable<TranslationDelta> ProcessStreamingAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var srcLang = request.SourceLanguage;
        if (string.IsNullOrEmpty(srcLang))
        {
            var detection = await _detector.DetectAsync(request.SourceText, ct).ConfigureAwait(false);
            srcLang = detection.Language;

            if (detection.Confidence < 0.6f)
                _logger.LogWarning(
                    "Low-confidence language detection: {Lang} ({Conf:P0}) – result may be unreliable.",
                    detection.Language, detection.Confidence);
            else
                _logger.LogDebug("Detected language: {Lang} ({Conf:P0})",
                    detection.Language, detection.Confidence);
        }

        if (srcLang == request.TargetLanguage)
        {
            yield return new TranslationDelta(request.SourceText);
            yield break;
        }

        ct.ThrowIfCancellationRequested();
        _modelReadiness.EnsureTranslationModelReady(srcLang, request.TargetLanguage);

        var segments = _segmenter.Segment(request.SourceText);
        TextSegment? prev = null;

        foreach (var segment in segments)
        {
            if (prev.HasValue)
            {
                var separator = prev.Value.BreakAfter == SegmentBreak.Paragraph ? "\n\n" : " ";
                yield return new TranslationDelta(separator);
            }

            await foreach (var delta in _engine.TranslateStreamingAsync(
                               segment.Text, srcLang, request.TargetLanguage, ct).ConfigureAwait(false))
            {
                yield return delta;
            }

            prev = segment;
        }
    }

    private async Task<string> TranslateSegmentsAsync(
        IReadOnlyList<TextSegment> segments,
        string srcLang,
        string targetLang,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var segment = segments[i];
            var partial = await _engine.TranslateAsync(
                segment.Text, srcLang, targetLang, ct);

            builder.Append(partial);

            if (i < segments.Count - 1)
            {
                var prevBreak = segment.BreakAfter;
                builder.Append(prevBreak == SegmentBreak.Paragraph ? "\n\n" : " ");
            }
        }
        return builder.ToString();
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
