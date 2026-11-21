using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Core.Dictation;

/// <summary>An active voice dictation session consuming 16 kHz mono float frames and updating a <see cref="DraftSession"/>.</summary>
public interface IDictationSession : IAsyncDisposable
{
    void Start();
    void Push(float[] frame);
    Task StopAsync();
}

/// <summary>
/// Per-session speech recognition options: full <see cref="Translation.Glossary"/> for tiered P0/P1 hotwords and
/// accent corrections, flat bounded <see cref="Hotwords"/> for local recognizers, and whether native ASR polishing
/// is active and directly satisfies the draft's <c>Clean</c> view without a second LLM pass.
/// </summary>
public sealed record SpeechSessionRequest(
    IReadOnlyList<string> Hotwords,
    Glossary? Glossary = null,
    bool EnableNativePolish = true,
    bool NativePolishSatisfiesClean = false)
{
    public static SpeechSessionRequest FromHotwords(IReadOnlyList<string> hotwords) => new(hotwords);
}

public interface IDictationSessionFactory
{
    /// <summary>
    /// Loads or connects the speech recognizer and returns a session that owns it; call off the UI thread.
    /// </summary>
    /// <exception cref="Models.ModelNotReadyException">A local speech model is not installed.</exception>
    Task<IDictationSession> CreateAsync(DraftSession draft, IReadOnlyList<string> glossary, CancellationToken ct);

    Task<IDictationSession> CreateAsync(DraftSession draft, SpeechSessionRequest request, CancellationToken ct) =>
        CreateAsync(draft, request.Hotwords, ct);
}
