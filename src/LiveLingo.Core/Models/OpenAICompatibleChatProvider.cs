using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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
        var apiKey = ResolveApiKey();
        using var httpRequest = BuildRequest(session, request, apiKey, stream: false);
        var response = await http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var result = LlamaServerChatResponse.GetAssistantText(doc.RootElement);
        result = LlamaServerChatResponse.ApplyTemplatePostProcessing(result, request.Profile.Descriptor.ChatTemplate);

        if (!string.IsNullOrWhiteSpace(result))
            return new ModelInvocationResult(result);

        logger.LogWarning(
            "Cloud model invocation returned empty output for {ModelId}. {Diag}",
            request.Profile.Id,
            LlamaServerChatResponse.DescribeFirstChoiceForLog(doc.RootElement));
        throw new InvalidOperationException("Cloud model invocation returned empty output.");
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        using var httpRequest = BuildRequest(session, request, apiKey, stream: true);
        var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await foreach (var delta in SseStreamReader.ReadDeltasAsync(body, ct).ConfigureAwait(false))
            yield return delta;
    }

    private string ResolveApiKey()
    {
        var apiKey = options.Value.CloudProviderApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Cloud provider API key is not configured.");
        return apiKey;
    }

    private static HttpRequestMessage BuildRequest(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        string apiKey,
        bool stream)
    {
        var payload = new OpenAICompatibleChatRequest(
            request.Profile.Id,
            request.Messages.Select(m => new OpenAICompatibleChatMessage(m.Role, m.Content)).ToArray(),
            request.Options.MaxTokens,
            request.Options.Temperature,
            request.Options.TopP,
            request.Options.StopSequences,
            stream);

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            OpenAICompatibleEndpoints.BuildChatCompletionsEndpoint(session.Endpoint));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(payload);
        return httpRequest;
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
