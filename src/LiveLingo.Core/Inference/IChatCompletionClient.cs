namespace LiveLingo.Core.Inference;

/// <param name="Prompt">The user message.</param>
/// <param name="Extra">Server-specific sampling fields (for example llama-server's top_k); empty for plain OpenAI endpoints.</param>
/// <param name="System">Optional system message sent before the user message.</param>
public sealed record ChatRequest(
    string Prompt,
    int MaxTokens,
    double Temperature,
    IReadOnlyDictionary<string, object> Extra,
    string? System = null);

public sealed record ChatCompletion(string Text, double FirstVisibleMs, double TotalMs, int OutputTokens);

/// <summary>Streams one chat completion from an OpenAI-compatible endpoint (local llama-server or cloud) and returns the full text.</summary>
public interface IChatCompletionClient
{
    /// <exception cref="HttpRequestException">The endpoint rejected or could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The stream was malformed or ended before a normal stop.</exception>
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct);
}
