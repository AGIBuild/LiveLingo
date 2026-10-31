using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Dictation;

public interface IDictationSessionFactory
{
    /// <summary>
    /// Loads the speech models (slow: seconds) and returns a session that owns them; call it off the UI thread.
    /// The glossary is given to the second-pass recognizer as hotwords.
    /// </summary>
    /// <exception cref="Models.ModelNotReadyException">A speech model is not installed.</exception>
    Task<DictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct);
}
