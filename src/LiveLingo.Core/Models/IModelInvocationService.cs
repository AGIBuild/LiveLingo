namespace LiveLingo.Core.Models;

public interface IModelInvocationService
{
    Task<ModelInvocationResult> InvokeAsync(
        ModelInvocationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Streams raw content deltas from the model.
    /// Each yielded string is a token fragment; callers must concatenate and post-process.
    /// </summary>
    IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelInvocationRequest request,
        CancellationToken ct = default);
}
