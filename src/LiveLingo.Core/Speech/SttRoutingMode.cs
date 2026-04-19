namespace LiveLingo.Core.Speech;

/// <summary>
/// Selects which speech-to-text engine the app should use. Each mode targets a
/// different quality/latency/coverage trade-off and resolves to a specific local model.
/// </summary>
public enum SttRoutingMode
{
    /// <summary>
    /// Prefer the highest accuracy model available. Currently maps to Cohere Transcribe
    /// 14-language int8 — top of the Open ASR Leaderboard, ~12 % WER on long-form English.
    /// Recommended default for translation use cases where a one-second extra latency is
    /// acceptable in exchange for materially fewer mis-transcriptions.
    /// </summary>
    AccuracyFirst,

    /// <summary>
    /// Prefer a streaming-capable engine that can emit partial transcripts mid-utterance.
    /// Reserved for the next phase (sherpa-onnx streaming Zipformer); not selectable until then.
    /// </summary>
    StreamingFirst,

    /// <summary>
    /// Prefer the engine with the broadest language coverage (NeMo Parakeet TDT or similar).
    /// Reserved for the next phase; not selectable until then.
    /// </summary>
    MultilingualFirst
}
