using LiveLingo.Core.Models;

namespace LiveLingo.Core.Dictation;

/// <summary>
/// Gets the speech models into memory before they are needed, so that dictation starts at once instead of after the
/// seconds they take to load.
/// </summary>
public interface ISpeechWarmup
{
    /// <summary>Loading while the models are being loaded, Ready once they are in memory.</summary>
    ModelResidency State { get; }

    /// <summary>Raised when <see cref="State"/> changed, on whichever thread caused it.</summary>
    event Action<ModelResidency>? StateChanged;

    /// <summary>
    /// Loads the speech models that are not in memory yet. The glossary is part of what is loaded (it is given to the
    /// second-pass recognizer as hotwords), so a changed glossary loads that model again. Kept in memory until the
    /// models have been idle for <see cref="CoreOptions.SpeechIdleTimeout"/>.
    /// </summary>
    /// <exception cref="Models.ModelNotReadyException">A speech model is not installed.</exception>
    Task WarmAsync(IReadOnlyList<string> glossary, CancellationToken ct = default);
}
