using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.Speech;

namespace LiveLingo.Core.Cloud;

public enum SpeechCloudProtocol
{
    /// <summary>Alibaba Bailian / DashScope duplex WebSocket (<c>/api-ws/v1/inference</c>) used by Qwen-Audio-3.1-ASR-Message and Streaming.</summary>
    QwenAudioWebSocket,

    /// <summary>Alibaba Bailian / DashScope Multimodal Generation HTTP (<c>/api/v1/services/aigc/multimodal-generation/generation</c>) used by Qwen-Audio-3.1-ASR-Flash.</summary>
    QwenAudioHttp,

    /// <summary>OpenAI-compatible chat completions with <c>input_audio</c> (such as <c>qwen3-asr-flash</c> on <c>/compatible-mode/v1</c>).</summary>
    OpenAiChatAudio,

    /// <summary>OpenAI-compatible multipart <c>/audio/transcriptions</c> endpoint (such as Whisper / GPT-4o-transcribe).</summary>
    OpenAiTranscriptions
}

/// <summary>
/// A user-configured cloud speech recognition endpoint. Audio is sent to it only when the user explicitly selected and
/// consented to a cloud endpoint for the Speech stage.
/// </summary>
public sealed record SpeechCloudRoute
{
    public const string DefaultBailianBaseUrl = "https://dashscope.aliyuncs.com/api/v1";
    public const string QwenAudio31MessageModel = "qwen-audio-3.1-asr-flash-message";
    public const string QwenAudio31StreamingModel = "qwen-audio-3.1-asr-flash-streaming";
    public const string QwenAudio31FlashModel = "qwen-audio-3.1-asr-flash";

    private SpeechCloudRoute(
        SpeechCloudProtocol protocol,
        Uri endpoint,
        string model,
        string displayModel,
        string apiKey)
    {
        Protocol = protocol;
        Endpoint = endpoint;
        Model = model;
        DisplayModel = displayModel;
        ApiKey = apiKey;
    }

    public SpeechCloudProtocol Protocol { get; }
    public Uri Endpoint { get; }

    /// <summary>Wire model identifier sent to the remote service.</summary>
    public string Model { get; }

    /// <summary>Concise model label shown in Composer and Settings diagnostics.</summary>
    public string DisplayModel { get; }

    public string ApiKey { get; }

    /// <summary>What the user calls this endpoint; null when unnamed.</summary>
    public string? Name { get; private init; }

    /// <summary>True when the model natively removes spoken fillers, repetitions and self-corrections during recognition.</summary>
    public bool SupportsNativePolish { get; private init; }

    /// <summary>True when the WebSocket session accepts <c>continue-task</c> context updates between utterances.</summary>
    public bool SupportsContextUpdate { get; private init; }

    /// <summary>True when the model accepts DashScope P0/P1 weighted <c>parameters.vocabulary</c> hotwords.</summary>
    public bool SupportsTieredHotwords { get; private init; }

    /// <summary>Resolved optimization strategy for this cloud speech endpoint.</summary>
    public ISpeechOptimizationStrategy Strategy => SpeechOptimizationStrategies.ForCloud(Protocol, Model);

    public static SpeechCloudRoute Create(string baseUrl, string model, string apiKey, string? name = null)
    {
        if (!Uri.TryCreate(baseUrl?.Trim().TrimEnd('/'), UriKind.Absolute, out var uri))
            throw new ArgumentException("The base URL must be an absolute URL.", nameof(baseUrl));

        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isWss = string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        var isHttpLoopback = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        var isWsLoopback = string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;

        if (!isHttps && !isWss && !isHttpLoopback && !isWsLoopback)
            throw new ArgumentException("The base URL must use https or wss (http/ws is allowed only for localhost).", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("A model or deployment name is required.", nameof(model));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("An API key is required.", nameof(apiKey));

        var (wireModel, displayModel, protocol, nativePolish, contextUpdate, tieredHotwords) =
            ResolveModel(uri, model.Trim(), isWss || isWsLoopback);
        var endpoint = BuildEndpoint(uri, protocol, isHttps || isWss);

        return new SpeechCloudRoute(protocol, endpoint, wireModel, displayModel, apiKey.Trim())
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            SupportsNativePolish = nativePolish,
            SupportsContextUpdate = contextUpdate,
            SupportsTieredHotwords = tieredHotwords
        };
    }

    public static SpeechCloudRoute CreateForLocalHost(LocalHostRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var model = route.ModelKey.Trim();
        var lower = model.ToLowerInvariant();
        var protocol = lower.Contains("qwen3-asr", StringComparison.Ordinal)
            || lower.Contains("qwen-audio", StringComparison.Ordinal)
            || lower.Contains("audio-chat", StringComparison.Ordinal)
            ? SpeechCloudProtocol.OpenAiChatAudio
            : SpeechCloudProtocol.OpenAiTranscriptions;

        var endpoint = protocol == SpeechCloudProtocol.OpenAiChatAudio
            ? route.ResolveEndpoint("/v1/chat/completions")
            : route.ResolveEndpoint("/v1/audio/transcriptions");

        return new SpeechCloudRoute(protocol, endpoint, model, model, route.BearerToken ?? "")
        {
            Name = route.HostDisplayName,
            IsLocalHost = true,
            SupportsNativePolish = false,
            SupportsContextUpdate = false,
            SupportsTieredHotwords = false
        };
    }

    /// <summary>True when this route targets a loopback local model host rather than a remote cloud service.</summary>
    public bool IsLocalHost { get; private init; }

    public static bool SupportsNativePolishForModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;
        var (_, _, _, nativePolish, _, _) = ResolveModel(new Uri(DefaultBailianBaseUrl), model.Trim(), forceWebSocket: false);
        return nativePolish;
    }

    private static (string WireModel, string DisplayModel, SpeechCloudProtocol Protocol, bool NativePolish, bool ContextUpdate, bool TieredHotwords)
        ResolveModel(Uri uri, string rawModel, bool forceWebSocket)
    {
        var lower = rawModel.ToLowerInvariant();

        // Friendly family aliases map directly to Qwen-Audio-3.1-ASR-Message (interactive voice input + native polish).
        if (lower is "qwen-audio-3.1-asr" or "qwen-audio-3.1-asr-message" or "qwen3.1-asr" or "qwen3.1-asr-message"
            or "qwen-audio-3.1-asr-flash-message")
        {
            return (
                QwenAudio31MessageModel,
                "Qwen-Audio-3.1-ASR",
                SpeechCloudProtocol.QwenAudioWebSocket,
                NativePolish: true,
                ContextUpdate: true,
                TieredHotwords: true);
        }

        if (lower is "qwen-audio-3.1-asr-streaming" or "qwen-audio-3.1-asr-flash-streaming")
        {
            return (
                QwenAudio31StreamingModel,
                "Qwen-Audio-3.1-ASR Streaming",
                SpeechCloudProtocol.QwenAudioWebSocket,
                NativePolish: false,
                ContextUpdate: false,
                TieredHotwords: true);
        }

        if (lower is "qwen-audio-3.1-asr-flash" or "qwen-audio-3.1-asr-next")
        {
            var next = lower.EndsWith("-next", StringComparison.Ordinal);
            return (
                lower,
                next ? "Qwen-Audio-3.1-ASR Next" : "Qwen-Audio-3.1-ASR Flash",
                forceWebSocket ? SpeechCloudProtocol.QwenAudioWebSocket : SpeechCloudProtocol.QwenAudioHttp,
                NativePolish: true,
                ContextUpdate: false,
                TieredHotwords: true);
        }

        if (lower == "qwen-audio-3.0-asr-flash")
        {
            return (
                lower,
                "Qwen-Audio-3.0-ASR",
                forceWebSocket ? SpeechCloudProtocol.QwenAudioWebSocket : SpeechCloudProtocol.QwenAudioHttp,
                NativePolish: false,
                ContextUpdate: false,
                TieredHotwords: true);
        }

        if (forceWebSocket
            || lower.StartsWith("qwen-audio-", StringComparison.Ordinal) && (lower.Contains("streaming", StringComparison.Ordinal) || lower.Contains("message", StringComparison.Ordinal))
            || lower.StartsWith("fun-asr-realtime", StringComparison.Ordinal)
            || lower.StartsWith("fun-asr-flash-8k-realtime", StringComparison.Ordinal)
            || lower.StartsWith("paraformer-realtime", StringComparison.Ordinal))
        {
            var isMessage = lower.Contains("message", StringComparison.Ordinal);
            return (
                rawModel,
                rawModel,
                SpeechCloudProtocol.QwenAudioWebSocket,
                NativePolish: isMessage && lower.Contains("3.1", StringComparison.Ordinal),
                ContextUpdate: isMessage,
                TieredHotwords: true);
        }

        if (lower.Contains("qwen3-asr", StringComparison.Ordinal)
            || lower.Contains("audio-preview", StringComparison.Ordinal)
            || uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var display = lower.Contains("qwen3-asr-1.7b", StringComparison.Ordinal) ? "Qwen3-ASR 1.7B"
                : lower.Contains("qwen3-asr-0.6b", StringComparison.Ordinal) ? "Qwen3-ASR 0.6B"
                : rawModel;
            return (
                rawModel,
                display,
                SpeechCloudProtocol.OpenAiChatAudio,
                NativePolish: false,
                ContextUpdate: false,
                TieredHotwords: false);
        }

        return (
            rawModel,
            rawModel,
            SpeechCloudProtocol.OpenAiTranscriptions,
            NativePolish: false,
            ContextUpdate: false,
            TieredHotwords: false);
    }

    private static Uri BuildEndpoint(Uri uri, SpeechCloudProtocol protocol, bool secure)
    {
        switch (protocol)
        {
            case SpeechCloudProtocol.QwenAudioWebSocket:
            {
                var scheme = secure ? "wss" : "ws";
                var host = string.Equals(uri.Host, "maas.qianwenaiapi.com", StringComparison.OrdinalIgnoreCase)
                    ? "cn-beijing.maas.qianwenaiapi.com"
                    : uri.Host;
                var portPart = uri.IsDefaultPort ? "" : $":{uri.Port}";
                var path = (string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
                           && !string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/"
                    ? uri.AbsolutePath
                    : "/api-ws/v1/inference";
                return new Uri($"{scheme}://{host}{portPart}{path}");
            }

            case SpeechCloudProtocol.QwenAudioHttp:
            {
                if (uri.AbsolutePath.EndsWith("/multimodal-generation/generation", StringComparison.OrdinalIgnoreCase))
                    return uri;
                return new Uri($"{uri.GetLeftPart(UriPartial.Authority)}/api/v1/services/aigc/multimodal-generation/generation");
            }

            case SpeechCloudProtocol.OpenAiChatAudio:
            {
                var trimmed = uri.ToString().TrimEnd('/');
                return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(trimmed)
                    : new Uri(trimmed + "/chat/completions");
            }

            default:
            {
                var trimmed = uri.ToString().TrimEnd('/');
                return trimmed.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(trimmed)
                    : new Uri(trimmed + "/audio/transcriptions");
            }
        }
    }

    public override string ToString() => $"SpeechCloudRoute {{ Protocol = {Protocol}, Endpoint = {Endpoint}, Model = {Model} }}";
}

public sealed class CloudSpeechException(string message, Exception? inner = null) : Exception(message, inner);
