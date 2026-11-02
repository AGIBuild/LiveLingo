namespace LiveLingo.Core.Inference;

public enum ModelStage { Speech, Cleaner, Translator }

public sealed record ModelUse(ModelStage Stage, string Name, bool Cloud, bool Confirmed);

/// <summary>Runtime facts reported where models are acquired or returned by a provider, never inferred from settings.</summary>
public sealed class ModelUsage
{
    public event Action<ModelUse>? Changed;

    public void Report(ModelUse model) => Changed?.Invoke(model);

    public void Report(InferenceRole role, string name, bool cloud, bool confirmed) =>
        Report(new ModelUse(role switch
        {
            InferenceRole.Cleaner => ModelStage.Cleaner,
            InferenceRole.Translator => ModelStage.Translator,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        }, name, cloud, confirmed));
}
