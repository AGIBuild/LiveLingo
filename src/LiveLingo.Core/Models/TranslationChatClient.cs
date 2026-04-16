using Microsoft.Extensions.AI;

namespace LiveLingo.Core.Models;

/// <summary>
/// MEA <see cref="IChatClient"/> adapter that routes calls through the domain
/// <see cref="IModelSelector"/> + <see cref="IModelInvocationService"/> stack.
///
/// Consumers pass routing hints via <see cref="ChatOptions.AdditionalProperties"/>:
///   "sourceLang"  – BCP-47 source language code (e.g. "zh")
///   "targetLang"  – BCP-47 target language code (e.g. "en")
///   "taskType"    – optional "Translation" | "PostProcessing" (default: Translation)
///
/// This class is the innermost client in the MEA middleware pipeline;
/// caching, logging and telemetry middleware sit above it.
/// </summary>
public sealed class TranslationChatClient : IChatClient
{
    private readonly IModelSelector _selector;
    private readonly IModelInvocationService _invocationService;

    public TranslationChatClient(IModelSelector selector, IModelInvocationService invocationService)
    {
        _selector = selector;
        _invocationService = invocationService;
    }

    public ChatClientMetadata Metadata { get; } = new("LiveLingo.TranslationChatClient");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var (src, tgt, taskType, routingContext) = ExtractContext(options);

        var profile = taskType == ModelTaskType.PostProcessing
            ? _selector.SelectPostProcessingProfile()
            : _selector.SelectTranslationProfile(src, tgt, routingContext);

        var defaults = ModelInvocationOptions.CreateTranslationDefaults();
        var invocationOptions = new ModelInvocationOptions(
            options?.MaxOutputTokens ?? defaults.MaxTokens,
            (float)(options?.Temperature ?? defaults.Temperature),
            defaults.TopP,
            defaults.StopSequences,
            false);

        var messages = messageList.Select(m => new ModelChatMessage(m.Role.Value, GetText(m))).ToArray();
        var request = new ModelInvocationRequest(profile, taskType, messages, invocationOptions);

        var result = await _invocationService.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.Text)) { ModelId = profile.Id };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not yet supported by TranslationChatClient.");

    public void Dispose() { }

    private static (string SourceLang, string TargetLang, ModelTaskType TaskType, TranslationRoutingContext? RoutingContext) ExtractContext(ChatOptions? options)
    {
        var props = options?.AdditionalProperties;
        var src = props?.TryGetValue("sourceLang", out var sv) == true ? sv as string ?? "zh" : "zh";
        var tgt = props?.TryGetValue("targetLang", out var tv) == true ? tv as string ?? "en" : "en";
        var taskTypeStr = props?.TryGetValue("taskType", out var tt) == true ? tt as string : null;
        var taskType = Enum.TryParse<ModelTaskType>(taskTypeStr, ignoreCase: true, out var parsed)
            ? parsed
            : ModelTaskType.Translation;

        TranslationRoutingContext? routingContext = null;
        if (props?.TryGetValue("textLength", out var tl) == true && tl is int textLength)
        {
            var isHighQuality = props.TryGetValue("isHighQualityMode", out var hq) == true && hq is true;
            routingContext = TranslationRoutingContext.FromText(
                string.Empty, src, tgt, isHighQuality) with { TextLength = textLength };
        }

        return (src, tgt, taskType, routingContext);
    }

    private static string GetText(ChatMessage message)
    {
        foreach (var part in message.Contents ?? [])
            if (part is TextContent tc) return tc.Text;
        return message.Text ?? string.Empty;
    }
}
