using LiveLingo.Core.Engines;

namespace LiveLingo.Core.Translation;

public interface ITranslationPipeline
{
    /// <summary>
    /// Runs the translation + optional post-processing pipeline to completion.
    /// Pass <paramref name="progress"/> to receive ordered
    /// <see cref="TranslationLifecycleEvent"/>s (language detection, translation
    /// started) so the UI can reflect what the pipeline is currently blocked on.
    /// </summary>
    Task<TranslationResult> ProcessAsync(
        TranslationRequest request,
        CancellationToken ct = default,
        IProgress<TranslationLifecycleEvent>? progress = null);

    /// <summary>
    /// Streams translation deltas for translation-only requests (no post-processing).
    /// Each <see cref="TranslationDelta"/> is a token fragment; the last update with
    /// <see cref="TranslationDelta.IsReplacement"/> = true signals that the caller
    /// should replace all previously displayed text.
    /// Post-processing options in <paramref name="request"/> are silently ignored;
    /// callers should use <see cref="ProcessAsync"/> when post-processing is required.
    /// When <paramref name="progress"/> is supplied the pipeline also reports the
    /// <see cref="TranslationPhase.FirstTokenReceived"/> marker, which is otherwise
    /// invisible but critical for exposing model cold-start latency.
    /// </summary>
    IAsyncEnumerable<TranslationDelta> ProcessStreamingAsync(
        TranslationRequest request,
        CancellationToken ct = default,
        IProgress<TranslationLifecycleEvent>? progress = null);
}
