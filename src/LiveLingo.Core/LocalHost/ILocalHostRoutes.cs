using LiveLingo.Core.Inference;

namespace LiveLingo.Core.LocalHost;

/// <summary>Resolves whether an inference or speech role is explicitly routed to a local model host (Bionic, LM Studio, Ollama, or a local OpenAI-compatible server).</summary>
public interface ILocalHostRoutes
{
    /// <summary>Read on every request so settings changes take effect immediately. Returns null when not routed to a local host.</summary>
    Task<LocalHostRoute?> GetAsync(InferenceRole role, CancellationToken ct);

    /// <summary>Returns the configured local-host speech route (for example local Whisper/MLX-Audio/Speaches/OpenAI audio server), or null when speech runs via built-in ONNX or cloud.</summary>
    Task<LocalHostRoute?> GetSpeechAsync(CancellationToken ct) => Task.FromResult<LocalHostRoute?>(null);
}

/// <summary>Default registration when no local model host routing is configured.</summary>
public sealed class NoLocalHostRoutes : ILocalHostRoutes
{
    public Task<LocalHostRoute?> GetAsync(InferenceRole role, CancellationToken ct) =>
        Task.FromResult<LocalHostRoute?>(null);
}
