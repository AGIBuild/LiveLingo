using LiveLingo.Core.Models;

namespace LiveLingo.Core.LocalHost;

/// <summary>Supported local model host families discovered on loopback.</summary>
public enum LocalHostKind
{
    Bionic,
    LmStudio,
    Ollama,
    OpenAiLocal
}

/// <summary>Discovery lifecycle state of a local model host.</summary>
public enum LocalHostStatus
{
    Running,
    AuthRequired,
    InstalledNotServing,
    NotInstalled
}

/// <summary>Protocol capability tier negotiated with a running local model host.</summary>
public enum LocalHostApiTier
{
    NativeV1,
    LegacyV0,
    OllamaNative,
    OpenAiCompat
}

/// <summary>Suitability classification of a discovered local model for LiveLingo cleanup or translation.</summary>
public enum HostModelSuitability
{
    CatalogVerified,
    FlagshipBilingual,
    Compatible,
    SuboptimalWarning
}

/// <summary>Structured failure kind when a bound local model host cannot serve a request.</summary>
public enum LocalHostFailureKind
{
    ServerNotRunning,
    AuthRequired,
    ModelNotFound,
    ContextTooSmall,
    HostError
}

/// <summary>Public reasoning configuration exposed by a local model host (for example Bionic / LM Studio v1 API).</summary>
public sealed record LocalHostReasoningCapability(
    IReadOnlyList<string> AllowedOptions,
    string? Default)
{
    public bool SupportsOff =>
        AllowedOptions.Any(option => string.Equals(option, "off", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when reasoning options are exposed and cannot be turned off (for example pure reasoning-only models).</summary>
    public bool IsForcedOn => AllowedOptions.Count > 0 && !SupportsOff;
}

/// <summary>Normalized metadata for one local model discovered on a local model host.</summary>
public sealed record LocalHostModelInfo(
    string Key,
    string DisplayName,
    string? Publisher = null,
    string? Architecture = null,
    string? Format = null,
    string? Quantization = null,
    string? ParamsString = null,
    long SizeBytes = 0,
    int? MaxContextLength = null,
    bool IsLoaded = false,
    string? LoadedEngine = null,
    int? LoadedContextLength = null,
    int? ParallelSlots = null,
    LocalHostReasoningCapability? Reasoning = null)
{
    public int? EffectiveContextLength => LoadedContextLength ?? MaxContextLength;
}

/// <summary>Model suitability evaluation produced by <see cref="LocalHostModelMatcher"/>.</summary>
public sealed record EvaluatedHostModel(
    LocalHostModelInfo Model,
    HostModelSuitability Suitability,
    string? WarningReason = null)
{
    public bool IsSuitable => Suitability != HostModelSuitability.SuboptimalWarning;
}

/// <summary>Recommended model to download into a local host when no suitable model is available yet.</summary>
public sealed record LocalHostRecommendation(
    ModelDescriptor CatalogModel,
    string HuggingFaceRepo,
    string? RecommendedQuantization,
    string LmsGetCommand,
    string OllamaTag,
    string Reason);

/// <summary>Candidate loopback endpoint submitted to <see cref="LocalHostRegistry"/> for parallel discovery.</summary>
public sealed record LocalHostProbeTarget(
    string HostId,
    LocalHostKind Kind,
    string DisplayName,
    Uri BaseUri,
    string? BearerToken = null,
    bool IsInstalledHint = false,
    string? InstallPath = null,
    bool CanStartServer = false,
    bool CanOpenApp = false);

/// <summary>Discovered local model host instance and its normalized model inventory.</summary>
public sealed record LocalHostInstance(
    string HostId,
    LocalHostKind Kind,
    string DisplayName,
    Uri? BaseUri,
    LocalHostStatus Status,
    LocalHostApiTier ApiTier,
    IReadOnlyList<LocalHostModelInfo> Models,
    bool SupportsDownload = false,
    bool SupportsStartServer = false,
    bool SupportsOpenApp = false,
    string? InstallPath = null,
    string? ErrorMessage = null)
{
    public bool HasLoadedModels => Models.Any(m => m.IsLoaded);

    /// <summary>True when this host family can serve HTTP audio transcription for the Speech stage.</summary>
    public bool SupportsSpeech => LocalHostRoute.SupportsHttpSpeech(Kind);
}

/// <summary>Progress snapshot of a model download delegated to a local model host (Bionic, LM Studio, or Ollama).</summary>
public sealed record LocalHostDownloadJob(
    string JobId,
    string HostId,
    string ModelId,
    string Status,
    long DownloadedBytes = 0,
    long TotalBytes = 0,
    double? BytesPerSecond = null,
    string? Error = null)
{
    public bool IsCompleted => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "already_downloaded", StringComparison.OrdinalIgnoreCase);

    public bool IsFailed => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase);

    public bool IsRunning => !IsCompleted && !IsFailed;

    public double? Percentage => TotalBytes > 0
        ? Math.Clamp(DownloadedBytes * 100d / TotalBytes, 0, 100)
        : IsCompleted ? 100 : null;
}

/// <summary>Explicit service-level failure raised when a configured local host or its bound model cannot serve a request.</summary>
public sealed class LocalHostUnavailableException : Exception
{
    public LocalHostUnavailableException(
        LocalHostFailureKind kind,
        string hostId,
        string hostDisplayName,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        HostId = hostId;
        HostDisplayName = hostDisplayName;
    }

    public LocalHostFailureKind Kind { get; }
    public string HostId { get; }
    public string HostDisplayName { get; }
}
