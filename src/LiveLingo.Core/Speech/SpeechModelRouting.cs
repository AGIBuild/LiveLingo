using LiveLingo.Core.Models;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Single source of truth that maps a <see cref="SttRoutingMode"/> (and optional model-id override)
/// to a concrete <see cref="ModelDescriptor"/> in <see cref="ModelRegistry.SpeechToTextModels"/>.
/// Used by both the runtime selector (<see cref="ISpeechEngineSelector"/>) and the Settings UI so
/// the values shown in the UI always agree with what will actually run.
/// </summary>
public static class SpeechModelRouting
{
    /// <summary>
    /// Parses the routing-mode string persisted in <c>SpeechSettings.RoutingMode</c>. Falls back to
    /// <see cref="SttRoutingMode.AccuracyFirst"/> for unknown / empty input so that the UI and the
    /// runtime selector always agree on the same default.
    /// </summary>
    public static SttRoutingMode ParseRoutingMode(string? value) =>
        Enum.TryParse<SttRoutingMode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SttRoutingMode.AccuracyFirst;

    /// <summary>
    /// Resolves the active STT model for the given routing mode and optional override id. If
    /// <paramref name="overrideModelId"/> matches an entry in <see cref="ModelRegistry.SpeechToTextModels"/>,
    /// it wins; otherwise the routing-mode default is returned.
    /// </summary>
    public static ModelDescriptor Resolve(SttRoutingMode mode, string? overrideModelId)
    {
        if (!string.IsNullOrWhiteSpace(overrideModelId))
        {
            foreach (var candidate in ModelRegistry.SpeechToTextModels)
            {
                if (string.Equals(candidate.Id, overrideModelId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        return ResolveDefaultForMode(mode);
    }

    /// <summary>
    /// Resolves the default STT model for the given routing mode, ignoring any user override.
    /// </summary>
    /// <remarks>
    /// Mapping rationale (kept in sync with <see cref="ModelRegistry.SpeechToTextModels"/>):
    /// <list type="bullet">
    ///   <item><description><see cref="SttRoutingMode.AccuracyFirst"/> → Cohere Transcribe 14-Lang
    ///   int8 (~1.6 GB) — top of the Open ASR Leaderboard, broadest language coverage.</description></item>
    ///   <item><description><see cref="SttRoutingMode.MultilingualFirst"/> → SenseVoice Small int8
    ///   (~228 MB, CJK-tuned) — best-in-class for Chinese / Cantonese / Japanese / Korean while
    ///   staying compact; on-model language detection.</description></item>
    ///   <item><description><see cref="SttRoutingMode.StreamingFirst"/> → reserved for a future
    ///   streaming Zipformer bundle. Until that lands the selector falls back to Cohere so the
    ///   user gets the best available offline model rather than a hard failure.</description></item>
    /// </list>
    /// </remarks>
    public static ModelDescriptor ResolveDefaultForMode(SttRoutingMode mode) => mode switch
    {
        SttRoutingMode.AccuracyFirst => ModelRegistry.SherpaCohereTranscribe14LangInt8,
        SttRoutingMode.MultilingualFirst => ModelRegistry.SherpaSenseVoiceSmallInt8,
        SttRoutingMode.StreamingFirst => ModelRegistry.SherpaCohereTranscribe14LangInt8,
        _ => ModelRegistry.SherpaCohereTranscribe14LangInt8
    };
}
