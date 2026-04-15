using System.Net.Http.Json;
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
        var payload = new LlamaServerChatRequest(
            request.Messages.Select(m => new LlamaServerChatMessage(m.Role, m.Content)).ToArray(),
            request.Options.MaxTokens,
            request.Options.Temperature,
            request.Options.TopP,
            request.Options.StopSequences,
            request.Options.Stream);

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
}
