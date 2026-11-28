namespace LiveLingo.Core.Inference;

public enum ModelStage { Speech, Cleaner, Translator }

/// <summary>Where a model actually executed.</summary>
public enum ModelOrigin { BuiltInLocal, LocalHost, Cloud }

/// <param name="Name">The full name, for the details.</param>
/// <param name="ShortName">The name for one line of status text; null where there is no shorter one (a cloud model's name).</param>
/// <param name="Endpoint">For a cloud or shared local host model, the display name of the endpoint/host that served it.</param>
/// <param name="LocalHost">True when executed via a local model host (Bionic, LM Studio, Ollama, etc.) over loopback.</param>
public sealed record ModelUse(
    ModelStage Stage,
    string Name,
    bool Cloud,
    bool Confirmed,
    string? ShortName = null,
    string? Endpoint = null,
    bool LocalHost = false)
{
    public ModelOrigin Origin => Cloud ? ModelOrigin.Cloud : LocalHost ? ModelOrigin.LocalHost : ModelOrigin.BuiltInLocal;

    /// <summary>The name to show in one line of text.</summary>
    public string Label => ShortName ?? Name;
}

/// <summary>Runtime facts reported where models are acquired or returned by a provider, never inferred from settings.</summary>
public sealed class ModelUsage
{
    public event Action<ModelUse>? Changed;

    public void Report(ModelUse model) => Changed?.Invoke(model);

    public void Report(InferenceRole role, string name, bool cloud, bool confirmed, string? shortName = null, string? endpoint = null) =>
        Report(new ModelUse(StageOf(role), name, cloud, confirmed, shortName, endpoint));

    public void ReportLocalHost(InferenceRole role, string name, bool confirmed, string hostDisplayName, string? shortName = null) =>
        Report(new ModelUse(StageOf(role), name, Cloud: false, confirmed, shortName, hostDisplayName, LocalHost: true));

    private static ModelStage StageOf(InferenceRole role) => role switch
    {
        InferenceRole.Cleaner => ModelStage.Cleaner,
        InferenceRole.Translator => ModelStage.Translator,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
