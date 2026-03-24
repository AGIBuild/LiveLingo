namespace LiveLingo.Core.Processing;

public interface ILlamaServerProcessManager : IDisposable
{
    /// <summary>
    /// Gets the current HTTP endpoint URL (e.g. "http://127.0.0.1:50123") if the server is running.
    /// </summary>
    string? CurrentEndpointUrl { get; }

    ModelLoadState State { get; }

    event Action<ModelLoadState>? StateChanged;

    /// <summary>
    /// Starts the llama-server process with the specified model and settings.
    /// If the server is already running with the same model, it does nothing.
    /// If it's running with a different model, it stops the old one and starts a new one.
    /// </summary>
    Task EnsureServerRunningAsync(string modelPath, int contextSize, int inferenceThreads, CancellationToken ct = default);

    /// <summary>
    /// Gracefully stops the background llama-server process.
    /// </summary>
    Task StopServerAsync();
}