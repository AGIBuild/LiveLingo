using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

public sealed class OpenAICompatibleChatProvider(
    HttpClient http,
    IOptions<CoreOptions> options,
    ILogger<OpenAICompatibleChatProvider> logger) : IModelProvider
{
    public ModelProviderKind ProviderKind => ModelProviderKind.OpenAICompatible;

    public async Task<ModelInvocationResult> InvokeAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default)
    {
        var apiKey = options.Value.CloudProviderApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Cloud provider API key is not configured.");

        var payload = new OpenAICompatibleChatRequest(
            request.Profile.Id,
            request.Messages.Select(m => new OpenAICompatibleChatMessage(m.Role, m.Content)).ToArray(),
            request.Options.MaxTokens,
            request.Options.Temperature,
            request.Options.TopP,
            request.Options.StopSequences,
            request.Options.Stream);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            OpenAICompatibleEndpoints.BuildChatCompletionsEndpoint(session.Endpoint));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(payload);

        var response = await http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var result = LlamaServerChatResponse.GetAssistantText(doc.RootElement);
        result = LlamaServerChatResponse.StripQwenThinkTags(result);

        if (!string.IsNullOrWhiteSpace(result))
            return new ModelInvocationResult(result);

        logger.LogWarning(
            "Cloud model invocation returned empty output for {ModelId}. {Diag}",
            request.Profile.Id,
            LlamaServerChatResponse.DescribeFirstChoiceForLog(doc.RootElement));
        throw new InvalidOperationException("Cloud model invocation returned empty output.");
    }

    private sealed record OpenAICompatibleChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OpenAICompatibleChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] float Temperature,
        [property: JsonPropertyName("top_p")] float TopP,
        [property: JsonPropertyName("stop")] IReadOnlyList<string> Stop,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OpenAICompatibleChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
