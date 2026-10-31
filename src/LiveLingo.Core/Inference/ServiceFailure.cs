using LiveLingo.Core.Models;

namespace LiveLingo.Core.Inference;

public static class ServiceFailure
{
    /// <summary>
    /// True when the inference service is unavailable for every request (not installed, cannot start, cannot be
    /// reached), as opposed to a failure caused by one unit's content.
    /// </summary>
    public static bool IsUnavailable(Exception ex) =>
        ex is HttpRequestException or LlamaServerStartException or ModelNotReadyException;
}
