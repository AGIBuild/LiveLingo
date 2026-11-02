using LiveLingo.Core.Models;

namespace LiveLingo.Core.Inference;

public interface IInferenceWarmup
{
    /// <summary>
    /// Starts the local server of <paramref name="role"/> if it is not running, so that its first request does not wait
    /// for the model to load. Nothing is loaded when the role runs in the cloud. The server is unloaded again when it is
    /// idle (<see cref="LlamaServerHostOptions.IdleTimeout"/>).
    /// </summary>
    /// <exception cref="ModelNotReadyException">The runtime or the model is not installed.</exception>
    Task WarmAsync(InferenceRole role, CancellationToken ct = default);
}

/// <summary>The local model that serves each role.</summary>
public static class InferenceModels
{
    public static ModelDescriptor For(InferenceRole role, TranslatorTier tier) => role switch
    {
        InferenceRole.Cleaner => ModelCatalog.Cleaner,
        InferenceRole.Translator => ModelCatalog.Translator(tier),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
