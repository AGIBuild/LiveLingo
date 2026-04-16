using LiveLingo.Core.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Models;

/// <summary>
/// MEA <see cref="IChatClient"/> adapter that routes calls through the domain
/// <see cref="IModelSelector"/> + <see cref="IModelInvocationService"/> stack.
///
/// Consumers pass routing and prompt hints via <see cref="ChatOptions.AdditionalProperties"/>:
///   "sourceLang"     – BCP-47 source language code (e.g. "zh")
///   "targetLang"     – BCP-47 target language code (e.g. "en")
///   "taskType"       – optional "Translation" | "PostProcessing" (default: Translation)
///   "textLength"     – int, source text length for routing escalation
///   "isHighQualityMode" – bool, escalate to cloud quality tier
///   "sourceText"     – full source text; when present the prompt is rebuilt
///                      using <see cref="TranslationPromptBuilder"/> with the
///                      selected model's <see cref="LocalModelChatTemplate"/>.
///   "sourceLangName" – human-readable source language name (e.g. "Chinese")
///   "targetLangName" – human-readable target language name (e.g. "English")
///
/// This class is the innermost client in the MEA middleware pipeline;
/// caching, logging and telemetry middleware sit above it.
/// </summary>
public sealed class TranslationChatClient : IChatClient
{
    private readonly IModelSelector _selector;
    private readonly IModelInvocationService _invocationService;
    private readonly ILogger<TranslationChatClient>? _logger;

    public TranslationChatClient(
        IModelSelector selector,
        IModelInvocationService invocationService,
        ILogger<TranslationChatClient>? logger = null)
    {
        _selector = selector;
        _invocationService = invocationService;
        _logger = logger;
    }

    public ChatClientMetadata Metadata { get; } = new("LiveLingo.TranslationChatClient");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (src, tgt, taskType, routingContext) = ExtractRoutingContext(options);

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

        // Use model-optimal prompt if sourceText is provided; otherwise forward caller messages.
        var messages = BuildMessages(chatMessages, options, profile);
        var request = new ModelInvocationRequest(profile, taskType, messages, invocationOptions);

        var result = await _invocationService.InvokeAsync(request, cancellationToken).ConfigureAwait(false);

        // Quality guard: check output against source text; escalate to cloud on failure.
        if (taskType == ModelTaskType.Translation)
        {
            var sourceText = GetAdditionalString(options, "sourceText");
            if (!string.IsNullOrEmpty(sourceText))
            {
                var qc = TranslationQualityGuard.Check(sourceText, result.Text, src, tgt);
                if (!qc.IsAcceptable)
                {
                    _logger?.LogWarning(
                        "Quality guard rejected local translation for {Src}→{Tgt}: {Reason}. Auto-escalating to cloud.",
                        src, tgt, qc.FailureReason);

                    var cloudResult = await TryRetryWithCloudAsync(
                        chatMessages, options, taskType, invocationOptions, src, tgt, cancellationToken)
                        .ConfigureAwait(false);

                    if (cloudResult is not null)
                        return cloudResult;

                    // Cloud unavailable – return local result with a warning annotation.
                    _logger?.LogWarning("Cloud escalation unavailable; returning local result despite quality check failure.");
                }
            }
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, result.Text)) { ModelId = profile.Id };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming is not yet supported by TranslationChatClient.");

    public void Dispose() { }

    private async Task<ChatResponse?> TryRetryWithCloudAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        ModelTaskType taskType,
        ModelInvocationOptions invocationOptions,
        string src,
        string tgt,
        CancellationToken cancellationToken)
    {
        try
        {
            // Force cloud escalation via IsHighQualityMode
            var cloudContext = new TranslationRoutingContext(
                TextLength: GetAdditionalInt(options, "textLength"),
                IsHighQualityMode: true);
            var cloudProfile = _selector.SelectTranslationProfile(src, tgt, cloudContext);

            if (cloudProfile.RuntimeKind == ModelRuntimeKind.LlamaServer)
                return null; // No cloud profile available

            var cloudMessages = BuildMessages(chatMessages, options, cloudProfile);
            var cloudRequest = new ModelInvocationRequest(cloudProfile, taskType, cloudMessages, invocationOptions);
            var cloudResult = await _invocationService.InvokeAsync(cloudRequest, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Quality guard escalation succeeded using cloud profile {Id}.", cloudProfile.Id);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, cloudResult.Text)) { ModelId = cloudProfile.Id };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Quality guard cloud escalation failed.");
            return null;
        }
    }

    private static string? GetAdditionalString(ChatOptions? options, string key)
    {
        var props = options?.AdditionalProperties;
        return props?.TryGetValue(key, out var v) == true ? v as string : null;
    }

    private static int GetAdditionalInt(ChatOptions? options, string key)
    {
        var props = options?.AdditionalProperties;
        return props?.TryGetValue(key, out var v) == true && v is int i ? i : 0;
    }

    private static IReadOnlyList<ModelChatMessage> BuildMessages(
        IEnumerable<ChatMessage> incomingMessages,
        ChatOptions? options,
        ModelProfile profile)
    {
        var props = options?.AdditionalProperties;

        // If sourceText is provided, rebuild with model-optimal template.
        if (props?.TryGetValue("sourceText", out var st) == true && st is string sourceText
            && !string.IsNullOrEmpty(sourceText))
        {
            var srcName = props.TryGetValue("sourceLangName", out var sn) == true ? sn as string ?? "Chinese" : "Chinese";
            var tgtName = props.TryGetValue("targetLangName", out var tn) == true ? tn as string ?? "English" : "English";

            return TranslationPromptBuilder.Build(
                sourceText, srcName, tgtName, profile.Descriptor.ChatTemplate);
        }

        // Fallback: convert incoming MEA messages as-is.
        return (incomingMessages as IList<ChatMessage> ?? incomingMessages.ToList())
            .Select(m => new ModelChatMessage(m.Role.Value, GetText(m)))
            .ToArray();
    }

    private static (string SourceLang, string TargetLang, ModelTaskType TaskType, TranslationRoutingContext? RoutingContext) ExtractRoutingContext(ChatOptions? options)
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
