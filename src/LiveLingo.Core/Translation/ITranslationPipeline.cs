using LiveLingo.Core.Engines;

namespace LiveLingo.Core.Translation;

public interface ITranslationPipeline
{
    Task<TranslationResult> ProcessAsync(
        TranslationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Streams translation deltas for translation-only requests (no post-processing).
    /// Each <see cref="TranslationDelta"/> is a token fragment; the last update with
    /// <see cref="TranslationDelta.IsReplacement"/> = true signals that the caller
    /// should replace all previously displayed text.
    /// Post-processing options in <paramref name="request"/> are silently ignored;
    /// callers should use <see cref="ProcessAsync"/> when post-processing is required.
    /// </summary>
    IAsyncEnumerable<TranslationDelta> ProcessStreamingAsync(
        TranslationRequest request,
        CancellationToken ct = default);
}
