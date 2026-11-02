namespace LiveLingo.Core.Cleanup;

/// <param name="Target">The original text of the passages being polished, in order.</param>
/// <param name="Preceding">Read-only: the already finished text before the target.</param>
/// <param name="Summary">Read-only summary of the whole dictation; empty when the polish sees the whole dictation itself.</param>
/// <param name="Glossary">Terms that must be written as given.</param>
/// <param name="Style">The polish requirements (<see cref="PolishStyle"/>).</param>
public sealed record PolishRequest(
    string Target,
    string Preceding,
    string Summary,
    IReadOnlyList<string> Glossary,
    string Style);

/// <summary>
/// The polish of a finished dictation: a longer text than one passage, read in one piece. Unlike
/// <see cref="ITextCleaner"/> (which fixes one passage while the speaker is still talking) it may restructure
/// sentences across passage seams.
/// </summary>
public interface IDictationPolisher
{
    /// <summary>A short summary of what the text is about and which terms occur in it, to be read with each part of a long text.</summary>
    /// <exception cref="HttpRequestException">The inference endpoint could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The model output was incomplete.</exception>
    Task<string> SummarizeAsync(string text, CancellationToken ct);

    /// <exception cref="HttpRequestException">The inference endpoint could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The model output was incomplete.</exception>
    Task<string> PolishAsync(PolishRequest request, CancellationToken ct);
}
