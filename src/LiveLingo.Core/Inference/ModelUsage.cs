namespace LiveLingo.Core.Inference;

public enum ModelStage { Speech, Cleaner, Translator }

/// <param name="Name">The full name, for the details.</param>
/// <param name="ShortName">The name for one line of status text; null where there is no shorter one (a cloud model's name).</param>
/// <param name="Endpoint">For a cloud model, the name the user gave the endpoint that served it.</param>
public sealed record ModelUse(
    ModelStage Stage, string Name, bool Cloud, bool Confirmed, string? ShortName = null, string? Endpoint = null)
{
    /// <summary>The name to show in one line of text.</summary>
    public string Label => ShortName ?? Name;
}

/// <summary>Runtime facts reported where models are acquired or returned by a provider, never inferred from settings.</summary>
public sealed class ModelUsage
{
    public event Action<ModelUse>? Changed;

    public void Report(ModelUse model) => Changed?.Invoke(model);

    public void Report(InferenceRole role, string name, bool cloud, bool confirmed, string? shortName = null, string? endpoint = null) =>
        Report(new ModelUse(role switch
        {
            InferenceRole.Cleaner => ModelStage.Cleaner,
            InferenceRole.Translator => ModelStage.Translator,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        }, name, cloud, confirmed, shortName, endpoint));
}
