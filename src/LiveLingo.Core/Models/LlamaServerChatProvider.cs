using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models;

public sealed class LlamaServerChatProvider(
    HttpClient http,
    ILogger<LlamaServerChatProvider> logger) : IModelProvider
{
    public ModelProviderKind ProviderKind => ModelProviderKind.LlamaServer;

    public async Task<ModelInvocationResult> InvokeAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default)
    {
        var payload = BuildPayload(request, stream: false);
        var response = await http.PostAsJsonAsync($"{session.Endpoint}/v1/chat/completions", payload, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var result = LlamaServerChatResponse.GetAssistantText(doc.RootElement);
        result = LlamaServerChatResponse.ApplyTemplatePostProcessing(result, request.Profile.Descriptor.ChatTemplate);

        if (!string.IsNullOrWhiteSpace(result))
            return new ModelInvocationResult(result);

        logger.LogWarning(
            "Model invocation returned empty output for {ModelId}. {Diag}",
            request.Profile.Id,
            LlamaServerChatResponse.DescribeFirstChoiceForLog(doc.RootElement));
        throw new InvalidOperationException("Model invocation returned empty output.");
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = BuildPayload(request, stream: true);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{session.Endpoint}/v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await foreach (var delta in SseStreamReader.ReadDeltasAsync(body, ct).ConfigureAwait(false))
            yield return delta;
    }

    private static LlamaServerChatRequest BuildPayload(ModelInvocationRequest request, bool stream) =>
        new(
            request.Messages.Select(m => new LlamaServerChatMessage(m.Role, m.Content)).ToArray(),
            request.Options.MaxTokens,
            request.Options.Temperature,
            request.Options.TopP,
            request.Options.StopSequences,
            stream);
}
