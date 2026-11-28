namespace LiveLingo.Core.LocalHost;

/// <summary>
/// Validated loopback-only route to a model served by a local model host (Bionic, LM Studio, Ollama, or a local
/// OpenAI-compatible server). Enforces the strict loopback invariant so local host traffic never leaves the device.
/// </summary>
public sealed record LocalHostRoute
{
    private LocalHostRoute(
        string hostId,
        string hostDisplayName,
        LocalHostKind kind,
        Uri baseUri,
        string modelKey,
        LocalHostApiTier apiTier,
        bool reasoningOffSupported,
        bool isLoaded,
        int? parallelSlots,
        string? bearerToken)
    {
        HostId = hostId;
        HostDisplayName = hostDisplayName;
        Kind = kind;
        BaseUri = baseUri;
        ModelKey = modelKey;
        ApiTier = apiTier;
        ReasoningOffSupported = reasoningOffSupported;
        IsLoaded = isLoaded;
        ParallelSlots = parallelSlots;
        BearerToken = bearerToken;
    }

    public string HostId { get; }
    public string HostDisplayName { get; }
    public LocalHostKind Kind { get; }
    public Uri BaseUri { get; }
    public string ModelKey { get; }
    public LocalHostApiTier ApiTier { get; }
    public bool ReasoningOffSupported { get; }
    public bool IsLoaded { get; }
    public int? ParallelSlots { get; }
    public string? BearerToken { get; }

    /// <summary>
    /// True when this local host family can expose an OpenAI-compatible HTTP audio endpoint
    /// (<c>/v1/audio/transcriptions</c> or <c>/v1/chat/completions</c> with <c>input_audio</c>).
    /// Bionic, LM Studio, and Ollama restrict their public HTTP APIs to text and image inputs.
    /// </summary>
    public static bool SupportsHttpSpeech(LocalHostKind kind) => kind == LocalHostKind.OpenAiLocal;

    /// <summary>Validates that <paramref name="baseUrl"/> is an HTTP loopback URI suitable for zero-consent local use.</summary>
    public static bool TryParseLoopbackUri(string? baseUrl, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return false;

        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp || !parsed.IsLoopback || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        uri = new Uri(parsed.GetLeftPart(UriPartial.Authority));
        return true;
    }

    public static LocalHostRoute Create(
        string hostId,
        string hostDisplayName,
        LocalHostKind kind,
        string baseUrl,
        string modelKey,
        LocalHostApiTier apiTier = LocalHostApiTier.NativeV1,
        bool reasoningOffSupported = false,
        bool isLoaded = false,
        int? parallelSlots = null,
        string? bearerToken = null)
    {
        if (string.IsNullOrWhiteSpace(hostId))
            throw new ArgumentException("A host ID is required.", nameof(hostId));
        if (!TryParseLoopbackUri(baseUrl, out var baseUri) || baseUri is null)
            throw new ArgumentException("Local model host URLs must use http on a loopback address (127.0.0.1, localhost, or ::1).", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(modelKey))
            throw new ArgumentException("A model key is required.", nameof(modelKey));

        var display = string.IsNullOrWhiteSpace(hostDisplayName) ? hostId.Trim() : hostDisplayName.Trim();
        var token = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();
        return new LocalHostRoute(
            hostId.Trim(),
            display,
            kind,
            baseUri,
            modelKey.Trim(),
            apiTier,
            reasoningOffSupported,
            isLoaded,
            parallelSlots,
            token);
    }

    public Uri ResolveEndpoint(string relativePath) =>
        new(BaseUri, relativePath.StartsWith('/') ? relativePath : "/" + relativePath);

    public override string ToString() =>
        $"LocalHostRoute {{ HostId = {HostId}, Kind = {Kind}, BaseUri = {BaseUri}, ModelKey = {ModelKey} }}";
}
