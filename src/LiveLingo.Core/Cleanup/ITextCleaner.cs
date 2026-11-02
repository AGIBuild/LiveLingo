namespace LiveLingo.Core.Cleanup;

/// <param name="Target">The only text the cleaner may rewrite.</param>
/// <param name="Preceding">Read-only context already written before the target.</param>
/// <param name="Continues">
/// Whether the speaker went on speaking after the target. Only the fact is given, never the words:
/// a small model copies read-only text into its output, and a partial hypothesis changes from moment to moment.
/// </param>
/// <param name="Glossary">Terms that must be kept exactly as written.</param>
public sealed record CleanupRequest(
    string Target,
    string Preceding,
    bool Continues,
    IReadOnlyList<string> Glossary);

public interface ITextCleaner
{
    /// <exception cref="HttpRequestException">The inference endpoint could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The model output was incomplete.</exception>
    Task<string> CleanAsync(CleanupRequest request, CancellationToken ct);
}
