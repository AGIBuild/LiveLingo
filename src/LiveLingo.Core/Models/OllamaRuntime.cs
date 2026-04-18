using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

/// <summary>
/// Resolves the user-configured Ollama daemon endpoint and produces a
/// <see cref="ModelRuntimeSession"/> pointing at it. We never start or
/// manage the daemon ourselves.
/// </summary>
public sealed class OllamaRuntime(IOptions<CoreOptions> options) : IModelRuntime
{
    public ModelRuntimeKind RuntimeKind => ModelRuntimeKind.Ollama;

    public Task<ModelRuntimeSession> AcquireSessionAsync(
        ModelProfile profile,
        ModelTaskType taskType,
        CancellationToken ct = default)
    {
        var baseUrl = options.Value.OllamaBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Ollama base URL is not configured.");

        return Task.FromResult(new ModelRuntimeSession(profile, taskType, baseUrl));
    }
}
