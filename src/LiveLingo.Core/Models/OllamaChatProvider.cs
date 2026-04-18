using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models;

/// <summary>
/// Backend for the user-managed Ollama daemon (<c>ollama serve</c>).
/// Talks to <c>/api/chat</c> directly over <see cref="HttpClient"/> with NDJSON
/// streaming, mirroring <see cref="LlamaServerChatProvider"/>'s style so the
/// Ollama session endpoint is the sole source of truth for the request URL —
/// no reliance on <see cref="HttpClient.BaseAddress"/>, no per-instance
/// mutable binding, and therefore no races between concurrent translations.
/// The Ollama daemon lifecycle (install, start, model pull) is the user's
/// responsibility; we only connect to an already-running endpoint.
/// </summary>
public sealed class OllamaChatProvider(
    HttpClient http,
    ILogger<OllamaChatProvider> logger) : IModelProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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
        var url = $"{session.Endpoint.TrimEnd('/')}/api/chat";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(BuildRequestBody(request), options: JsonOptions),
        };

        using var response = await http
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(body, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatChunk>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping malformed Ollama NDJSON line: {Line}", line);
                continue;
            }

            if (chunk?.Message?.Content is { Length: > 0 } content)
                yield return content;

            if (chunk?.Done == true)
                yield break;
        }
    }

    private static object BuildRequestBody(ModelInvocationRequest request) => new
    {
        model = request.Profile.Id,
        messages = request.Messages
            .Select(m => new { role = m.Role.ToLowerInvariant(), content = m.Content })
            .ToArray(),
        stream = true,
        options = new
        {
            temperature = request.Options.Temperature,
            top_p = request.Options.TopP,
            num_predict = request.Options.MaxTokens,
            stop = request.Options.StopSequences.ToArray(),
        },
    };

    private sealed record OllamaChatChunk
    {
        [JsonPropertyName("message")] public OllamaChatMessage? Message { get; init; }
        [JsonPropertyName("done")] public bool Done { get; init; }
    }

    private sealed record OllamaChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;
    }
}
