namespace LiveLingo.Core.Models;

public interface IModelProvider
{
    ModelProviderKind ProviderKind { get; }

    Task<ModelInvocationResult> InvokeAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Streams token deltas from the model using SSE (stream=true).
    /// Each yielded string is a raw content delta as received from the model.
    /// Template post-processing (e.g. think-tag removal) is NOT applied here;
    /// callers are responsible for assembling the full text and post-processing it.
    /// </summary>
    IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default);
}
