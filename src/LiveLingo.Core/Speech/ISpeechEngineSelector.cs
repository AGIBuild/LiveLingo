using LiveLingo.Core.Models;

namespace LiveLingo.Core.Speech;

/// <summary>
/// Resolves which <see cref="ISpeechToTextEngine"/> the speech pipeline should use right now.
/// Lets <see cref="ModelDescriptor"/> selection live in one place instead of being scattered
/// across coordinators, settings consumers, and download flows.
/// </summary>
public interface ISpeechEngineSelector
{
    /// <summary>
    /// The engine that the speech pipeline should call into for the next transcription request.
    /// Always returns a usable engine; throws when no engine implementation can satisfy the
    /// current routing mode.
    /// </summary>
    ISpeechToTextEngine GetEngine();

    /// <summary>
    /// The model descriptor backing <see cref="GetEngine"/>; used by the coordinator to drive
    /// "is this model installed?" checks and "ensure / download this model" flows.
    /// </summary>
    ModelDescriptor GetActiveModel();

    /// <summary>
    /// Currently configured routing mode. Surfaced for diagnostics / UI labels; the engine
    /// resolution itself goes through <see cref="GetEngine"/> and <see cref="GetActiveModel"/>.
    /// </summary>
    SttRoutingMode CurrentMode { get; }
}
