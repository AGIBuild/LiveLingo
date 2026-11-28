namespace LiveLingo.Core.Inference;

/// <param name="Prompt">The user message.</param>
/// <param name="Extra">Server-specific sampling fields (for example llama-server's top_k); empty for plain OpenAI endpoints.</param>
/// <param name="System">Optional system message sent before the user message.</param>
/// <param name="OnDelta">Optional callback invoked sequentially as each non-empty content delta arrives from the SSE stream.</param>
public sealed record ChatRequest(
    string Prompt,
    int MaxTokens,
    double Temperature,
    IReadOnlyDictionary<string, object> Extra,
    string? System = null,
    Action<string>? OnDelta = null);

public sealed record ChatCompletion(string Text, double FirstVisibleMs, double TotalMs, int OutputTokens, string? Model = null);

/// <summary>Streams one chat completion from an OpenAI-compatible endpoint (local llama-server or cloud) and returns the full text.</summary>
public interface IChatCompletionClient
{
    /// <exception cref="HttpRequestException">The endpoint rejected or could not serve the request.</exception>
    /// <exception cref="InvalidDataException">The stream was malformed or ended before a normal stop.</exception>
    Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct);

    /// <summary>
    /// Pre-fills a local inference server's KV cache with <paramref name="systemPrompt"/> and <paramref name="userPrefix"/>
    /// while dictation is recording, so post-stop polishing only prefills the newly spoken words.
    /// Cloud endpoints never receive warm-up traffic.
    /// </summary>
    Task WarmPromptAsync(
        string systemPrompt,
        IReadOnlyDictionary<string, object>? extra = null,
        CancellationToken ct = default,
        string userPrefix = "目标：") => Task.CompletedTask;
}
