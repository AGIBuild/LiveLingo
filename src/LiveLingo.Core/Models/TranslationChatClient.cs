using System.Runtime.CompilerServices;
using System.Text;
using LiveLingo.Core.Processing;
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
    private readonly ITranslationInvoker _translationInvoker;
    private readonly ITranslationGlossary? _glossary;
    private readonly ILogger<TranslationChatClient>? _logger;

    public TranslationChatClient(
        IModelSelector selector,
        IModelInvocationService invocationService,
        ITranslationInvoker translationInvoker,
        ITranslationGlossary? glossary = null,
        ILogger<TranslationChatClient>? logger = null)
    {
        _selector = selector;
        _invocationService = invocationService;
        _translationInvoker = translationInvoker;
        _glossary = glossary;
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
        var invocationOptions = BuildInvocationOptions(options, stream: false);

        // Post-processing does not currently use runtime fallback — the selector already
        // applies routing policy and post-processing has its own cloud flag.
        if (taskType == ModelTaskType.PostProcessing)
        {
            var ppProfile = _selector.SelectPostProcessingProfile();
            var ppMessages = BuildMessages(chatMessages, options, ppProfile, _glossary);
            var ppRequest = new ModelInvocationRequest(ppProfile, taskType, ppMessages, invocationOptions);
            var ppResult = await _invocationService.InvokeAsync(ppRequest, cancellationToken).ConfigureAwait(false);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ppResult.Text)) { ModelId = ppProfile.Id };
        }

        var plan = _selector.BuildTranslationRoutePlan(src, tgt, routingContext);
        var sourceText = GetAdditionalString(options, "sourceText");
        var qualityGuard = BuildQualityGuard(sourceText, src, tgt);

        var outcome = await _translationInvoker.InvokeAsync(
            plan,
            candidate => new ModelInvocationRequest(
                candidate.Profile,
                taskType,
                BuildMessages(chatMessages, options, candidate.Profile, _glossary),
                invocationOptions),
            qualityGuard,
            cancellationToken).ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, outcome.Text))
        {
            ModelId = outcome.Candidate.Profile.Id
        };
    }

    /// <summary>
    /// Streams translation tokens from the underlying model.
    ///
    /// Strategy by chat template:
    /// • <see cref="LocalModelChatTemplate.Qwen"/>: buffers the entire response
    ///   (Qwen outputs a &lt;think&gt; reasoning preamble that must be stripped before
    ///   any text is forwarded to the UI), then yields the stripped result as a
    ///   single <see cref="ChatResponseUpdate"/>.
    /// • All other templates: yields raw deltas as they arrive, applying quality guard
    ///   on the assembled result; if the guard fails the cloud result replaces the stream.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (src, tgt, taskType, routingContext) = ExtractRoutingContext(options);
        var invocationOptions = BuildInvocationOptions(options, stream: true);

        // Post-processing streams directly without fallback orchestration.
        if (taskType == ModelTaskType.PostProcessing)
        {
            var ppProfile = _selector.SelectPostProcessingProfile();
            var ppMessages = BuildMessages(chatMessages, options, ppProfile, _glossary);
            var ppRequest = new ModelInvocationRequest(ppProfile, taskType, ppMessages, invocationOptions);
            await foreach (var delta in _invocationService.InvokeStreamingAsync(ppRequest, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(delta))
                    yield return new ChatResponseUpdate(ChatRole.Assistant, delta) { ModelId = ppProfile.Id };
            }
            yield break;
        }

        var plan = _selector.BuildTranslationRoutePlan(src, tgt, routingContext);

        // Qwen requires buffered think-tag stripping; if the primary is Qwen we fall
        // back to a non-streaming invocation path (same plan, same fallback semantics)
        // and emit the cleaned output as a single update. Non-Qwen primaries stream.
        if (plan.Primary.Profile.Descriptor.ChatTemplate == LocalModelChatTemplate.Qwen)
        {
            var bufferedOptions = invocationOptions with { Stream = false };
            var bufferedOutcome = await _translationInvoker.InvokeAsync(
                plan,
                candidate => new ModelInvocationRequest(
                    candidate.Profile,
                    taskType,
                    BuildMessages(chatMessages, options, candidate.Profile, _glossary),
                    bufferedOptions),
                BuildQualityGuard(GetAdditionalString(options, "sourceText"), src, tgt),
                cancellationToken).ConfigureAwait(false);

            var cleaned = LlamaServerChatResponse.StripQwenThinkTags(bufferedOutcome.Text).Trim();
            if (!string.IsNullOrEmpty(cleaned))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, cleaned)
                {
                    ModelId = bufferedOutcome.Candidate.Profile.Id
                };
            }
            yield break;
        }

        await foreach (var update in _translationInvoker.InvokeStreamingAsync(
                           plan,
                           candidate => new ModelInvocationRequest(
                               candidate.Profile,
                               taskType,
                               BuildMessages(chatMessages, options, candidate.Profile, _glossary),
                               invocationOptions),
                           BuildQualityGuard(GetAdditionalString(options, "sourceText"), src, tgt),
                           cancellationToken).ConfigureAwait(false))
        {
            var chatUpdate = new ChatResponseUpdate(ChatRole.Assistant, update.Delta)
            {
                ModelId = update.Candidate.Profile.Id
            };
            if (update.ReplaceAll)
            {
                chatUpdate.AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["replaceAll"] = true
                };
            }
            yield return chatUpdate;
        }
    }

    public void Dispose() { }

    private static ModelInvocationOptions BuildInvocationOptions(ChatOptions? options, bool stream)
    {
        var defaults = ModelInvocationOptions.CreateTranslationDefaults();
        return new ModelInvocationOptions(
            options?.MaxOutputTokens ?? defaults.MaxTokens,
            (float)(options?.Temperature ?? defaults.Temperature),
            defaults.TopP,
            defaults.StopSequences,
            stream);
    }

    private TranslationQualityAssertion? BuildQualityGuard(string? sourceText, string src, string tgt)
    {
        if (string.IsNullOrEmpty(sourceText)) return null;

        return translated =>
        {
            var cleaned = (translated ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(cleaned)) return true;
            var qc = TranslationQualityGuard.Check(sourceText, cleaned, src, tgt);
            if (!qc.IsAcceptable)
            {
                _logger?.LogWarning(
                    "Quality guard rejected translation for {Src}→{Tgt}: {Reason}.",
                    src, tgt, qc.FailureReason);
            }
            return qc.IsAcceptable;
        };
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
        ModelProfile profile,
        ITranslationGlossary? glossary)
    {
        var props = options?.AdditionalProperties;

        // If sourceText is provided, rebuild with model-optimal template.
        if (props?.TryGetValue("sourceText", out var st) == true && st is string sourceText
            && !string.IsNullOrEmpty(sourceText))
        {
            var srcName = props.TryGetValue("sourceLangName", out var sn) == true ? sn as string ?? "Chinese" : "Chinese";
            var tgtName = props.TryGetValue("targetLangName", out var tn) == true ? tn as string ?? "English" : "English";
            var srcLang = props.TryGetValue("sourceLang", out var sl) == true ? sl as string ?? "zh" : "zh";
            var tgtLang = props.TryGetValue("targetLang", out var tl) == true ? tl as string ?? "en" : "en";

            var hints = glossary?.GetRelevantEntries(sourceText, srcLang, tgtLang);

            return TranslationPromptBuilder.Build(
                sourceText, srcName, tgtName, profile.Descriptor.ChatTemplate, hints);
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
                string.Empty, src, tgt, isHighQuality) with
            { TextLength = textLength };
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
