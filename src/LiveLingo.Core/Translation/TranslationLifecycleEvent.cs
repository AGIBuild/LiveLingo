namespace LiveLingo.Core.Translation;

/// <summary>
/// Ordered phase markers a translation pass can pass back to the caller
/// through <see cref="System.IProgress{T}"/>. They surface waits that are
/// otherwise invisible (language detection, model startup, first-token
/// latency) so the UI can show a meaningful status instead of a frozen
/// "translating…" message.
/// </summary>
public enum TranslationPhase
{
    /// <summary>Detector call is about to start (only when the caller did not pass a source language).</summary>
    LanguageDetectionStarted,

    /// <summary>Detector returned. <see cref="TranslationLifecycleEvent.DetectedLanguage"/> / <see cref="TranslationLifecycleEvent.DetectionConfidence"/> are populated.</summary>
    LanguageDetected,

    /// <summary>About to issue the first engine call (model readiness has passed).</summary>
    TranslationStarted,

    /// <summary>Streaming only: the engine produced its first delta, so the model is warm and output is flowing.</summary>
    FirstTokenReceived,
}

/// <param name="Phase">The phase just entered.</param>
/// <param name="Elapsed">Time since the pipeline began for this request.</param>
/// <param name="DetectedLanguage">Detected source language (only set for <see cref="TranslationPhase.LanguageDetected"/>).</param>
/// <param name="DetectionConfidence">Confidence of the language detection (only set for <see cref="TranslationPhase.LanguageDetected"/>).</param>
public sealed record TranslationLifecycleEvent(
    TranslationPhase Phase,
    TimeSpan Elapsed,
    string? DetectedLanguage = null,
    float? DetectionConfidence = null);
