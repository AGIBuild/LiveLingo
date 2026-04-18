using System.Runtime.CompilerServices;
using System.Text;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace LiveLingo.Core.Models;

/// <summary>
/// Backend for the user-managed Ollama daemon (<c>ollama serve</c>).
/// Uses <c>OllamaSharp</c> for native <c>/api/chat</c> calls with streaming.
/// The Ollama daemon lifecycle (install, start, model pull) is the user's
/// responsibility – we only connect to an already-running endpoint.
/// </summary>
public sealed class OllamaChatProvider(
    HttpClient http,
    ILogger<OllamaChatProvider> logger) : IModelProvider
{
    public ModelProviderKind ProviderKind => ModelProviderKind.Ollama;

    public async Task<ModelInvocationResult> InvokeAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default)
    {
        var buffer = new StringBuilder();
        await foreach (var delta in StreamAsync(session, request, ct).ConfigureAwait(false))
            buffer.Append(delta);

        var result = LlamaServerChatResponse.ApplyTemplatePostProcessing(
            buffer.ToString(), request.Profile.Descriptor.ChatTemplate);

        if (!string.IsNullOrWhiteSpace(result))
            return new ModelInvocationResult(result);

        logger.LogWarning(
            "Ollama model invocation returned empty output for tag {ModelTag}.",
            request.Profile.Id);
        throw new InvalidOperationException("Ollama model invocation returned empty output.");
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var delta in StreamAsync(session, request, ct).ConfigureAwait(false))
            yield return delta;
    }

    private async IAsyncEnumerable<string> StreamAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureBaseAddress(session.Endpoint);
        var client = new OllamaApiClient(http, request.Profile.Id);
        var chatRequest = BuildChatRequest(request);

        await foreach (var response in client.ChatAsync(chatRequest, ct).ConfigureAwait(false))
        {
            if (response?.Message?.Content is { Length: > 0 } content)
                yield return content;
        }
    }

    /// <summary>
    /// OllamaSharp reads the endpoint from <see cref="HttpClient.BaseAddress"/>.
    /// The Ollama base URL is effectively immutable within a session (sourced from
    /// <see cref="CoreOptions.OllamaBaseUrl"/>), so we assign it once per HttpClient
    /// and reuse across all invocations; concurrent calls pointing at the same
    /// endpoint are a no-op after the first assignment.
    /// </summary>
    private void EnsureBaseAddress(string endpoint)
    {
        var uri = new Uri(endpoint);
        if (http.BaseAddress is null)
        {
            http.BaseAddress = uri;
            return;
        }

        if (http.BaseAddress != uri)
        {
            throw new InvalidOperationException(
                $"Ollama HTTP client base address is already bound to {http.BaseAddress}; " +
                $"cannot switch to {uri} at runtime. Restart the app after changing Ollama base URL.");
        }
    }

    private static ChatRequest BuildChatRequest(ModelInvocationRequest request) =>
        new()
        {
            Model = request.Profile.Id,
            Messages = request.Messages.Select(m => new Message(MapRole(m.Role), m.Content)).ToArray(),
            Stream = true,
            Options = new RequestOptions
            {
                Temperature = request.Options.Temperature,
                TopP = request.Options.TopP,
                NumPredict = request.Options.MaxTokens,
                Stop = request.Options.StopSequences.ToArray(),
            },
        };

    private static ChatRole MapRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };
}
