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
        TranslationRequest request,
        CancellationToken ct = default,
        IProgress<TranslationLifecycleEvent>? progress = null)
    {
        var reporter = new LifecycleReporter(progress);
        var prep = await PrepareAsync(request, reporter, ct).ConfigureAwait(false);
        if (prep.IsIdentity)
        {
            return new TranslationResult(
                request.SourceText, prep.SourceLanguage, request.SourceText,
                TimeSpan.Zero, null);
        }

        if (request.PostProcessing is not null)
            _modelReadiness.EnsurePostProcessingModelReady();

        reporter.Report(TranslationPhase.TranslationStarted);
        var sw = Stopwatch.StartNew();
        string translated;
        try
        {
            translated = await TranslateUnitsAsync(
                prep, request.TargetLanguage, ct).ConfigureAwait(false);
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
            finalText, prep.SourceLanguage, translated,
            translationDuration, postDuration);
    }

    public async IAsyncEnumerable<TranslationDelta> ProcessStreamingAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default,
        IProgress<TranslationLifecycleEvent>? progress = null)
    {
        var reporter = new LifecycleReporter(progress);
        var prep = await PrepareAsync(request, reporter, ct).ConfigureAwait(false);
        if (prep.IsIdentity)
        {
            yield return new TranslationDelta(request.SourceText);
            yield break;
        }

        reporter.Report(TranslationPhase.TranslationStarted);
        var firstTokenReported = false;
        TranslationUnit? prev = null;
        foreach (var unit in prep.Units)
        {
            if (prev is { } p)
            {
                var separator = TextSegmenter.JoinSeparatorFor(
                    p.BreakAfter, request.TargetLanguage);
                if (separator.Length > 0)
                    yield return new TranslationDelta(separator);
            }

            await foreach (var delta in _engine.TranslateStreamingAsync(
                               unit.SourceText, prep.SourceLanguage, request.TargetLanguage, ct)
                               .ConfigureAwait(false))
            {
                if (!firstTokenReported)
                {
                    firstTokenReported = true;
                    reporter.Report(TranslationPhase.FirstTokenReceived);
                }
                yield return delta;
            }

            prev = unit;
        }
    }

    /// <summary>
    /// Shared front-half of both <see cref="ProcessAsync"/> and
    /// <see cref="ProcessStreamingAsync"/>: detect language when absent,
    /// short-circuit identity translations, enforce translation-model
    /// readiness, and plan segments + units. Post-processing readiness is
    /// deliberately NOT checked here because streaming does not run the
    /// post-processing stage.
    /// </summary>
    private async Task<PreparedRequest> PrepareAsync(
        TranslationRequest request, LifecycleReporter reporter, CancellationToken ct)
    {
        var srcLang = request.SourceLanguage;
        if (string.IsNullOrEmpty(srcLang))
        {
            reporter.Report(TranslationPhase.LanguageDetectionStarted);
            var detection = await _detector.DetectAsync(request.SourceText, ct).ConfigureAwait(false);
            srcLang = detection.Language;
            reporter.Report(
                TranslationPhase.LanguageDetected,
                detection.Language,
                detection.Confidence);

            if (detection.Confidence < 0.6f)
                _logger.LogWarning(
                    "Low-confidence language detection: {Lang} ({Conf:P0}) – result may be unreliable.",
                    detection.Language, detection.Confidence);
            else
                _logger.LogDebug("Detected language: {Lang} ({Conf:P0})",
                    detection.Language, detection.Confidence);
        }

        if (srcLang == request.TargetLanguage)
            return PreparedRequest.Identity(srcLang);

        ct.ThrowIfCancellationRequested();
        _modelReadiness.EnsureTranslationModelReady(srcLang, request.TargetLanguage);

        var segments = _segmenter.Segment(request.SourceText);
        var units = _segmenter.PlanUnits(segments);
        return new PreparedRequest(false, srcLang, segments, units);
    }

    /// <summary>
    /// Thin wrapper that lets <see cref="PrepareAsync"/> and the translate loop
    /// emit timestamped lifecycle events without worrying about null checks or
    /// keeping an external Stopwatch in sync.
    /// </summary>
    private sealed class LifecycleReporter(IProgress<TranslationLifecycleEvent>? progress)
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public void Report(
            TranslationPhase phase,
            string? detectedLanguage = null,
            float? confidence = null)
        {
            progress?.Report(new TranslationLifecycleEvent(
                phase, _sw.Elapsed, detectedLanguage, confidence));
        }
    }

    /// <summary>
    /// Translates each unit in one engine call, splits the response on '\n'
    /// to recover per-segment fragments, and reassembles the final output
    /// using the reassembly separators declared by the segmenter. When the
    /// engine fails to preserve the unit's internal line count (common when
    /// small local LLMs reformat short multi-line inputs), the entire unit
    /// output is attributed to the first fragment so content is never lost.
    /// </summary>
    private async Task<string> TranslateUnitsAsync(
        PreparedRequest prep, string targetLang, CancellationToken ct)
    {
        var fragmentOutputs = new string[prep.Segments.Count];

        foreach (var unit in prep.Units)
        {
            ct.ThrowIfCancellationRequested();
            var raw = await _engine.TranslateAsync(
                unit.SourceText, prep.SourceLanguage, targetLang, ct).ConfigureAwait(false);
            AssignUnitOutput(fragmentOutputs, unit, raw);
        }

        var builder = new StringBuilder();
        for (var i = 0; i < prep.Segments.Count; i++)
        {
            builder.Append(fragmentOutputs[i]);
            if (i < prep.Segments.Count - 1)
                builder.Append(TextSegmenter.JoinSeparatorFor(
                    prep.Segments[i].BreakAfter, targetLang));
        }
        return builder.ToString();
    }

    private static void AssignUnitOutput(
        string[] fragmentOutputs, TranslationUnit unit, string raw)
    {
        if (unit.SegmentCount == 1)
        {
            fragmentOutputs[unit.FirstSegmentIndex] = raw;
            return;
        }

        var parts = raw.Split('\n');
        if (parts.Length == unit.SegmentCount)
        {
            for (var k = 0; k < unit.SegmentCount; k++)
                fragmentOutputs[unit.FirstSegmentIndex + k] = parts[k];
            return;
        }

        // Engine did not honour the requested newline layout. Keep every
        // character the model produced on the first fragment and leave the
        // rest blank – the reassembler will still insert Line separators,
        // so the user sees "<full translation>\n" (trailing newline trimmed
        // by the caller) instead of silently losing text.
        fragmentOutputs[unit.FirstSegmentIndex] = raw;
        for (var k = 1; k < unit.SegmentCount; k++)
            fragmentOutputs[unit.FirstSegmentIndex + k] = string.Empty;
    }

    private readonly record struct PreparedRequest(
        bool IsIdentity,
        string SourceLanguage,
        IReadOnlyList<TextSegment> Segments,
        IReadOnlyList<TranslationUnit> Units)
    {
        public static PreparedRequest Identity(string sourceLanguage) =>
            new(true, sourceLanguage, [], []);
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
