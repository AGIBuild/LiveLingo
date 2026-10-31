namespace LiveLingo.Core.Inference;

/// <summary>Each role runs in its own llama-server process so models can be loaded, switched and unloaded independently.</summary>
public enum InferenceRole
{
    Translator
}

public sealed record LlamaServerLaunch(string ExecutablePath, IReadOnlyList<string> Arguments, Uri Endpoint);

public interface ILlamaProcess : IAsyncDisposable
{
    bool HasExited { get; }
    int? ExitCode { get; }
}

public interface ILlamaProcessLauncher
{
    ILlamaProcess Start(LlamaServerLaunch launch);
}

public interface IServerHealthProbe
{
    /// <summary>True when the server answers its health endpoint with success (the model is loaded).</summary>
    Task<bool> IsReadyAsync(Uri endpoint, CancellationToken ct);
}

public interface IPortAllocator
{
    int NextLoopbackPort();
}

public sealed class LlamaServerStartException(string message, Exception? inner = null) : Exception(message, inner);
