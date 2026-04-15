namespace LiveLingo.Core.Models;

public interface IModelProvider
{
    ModelProviderKind ProviderKind { get; }

    Task<ModelInvocationResult> InvokeAsync(
        ModelRuntimeSession session,
        ModelInvocationRequest request,
        CancellationToken ct = default);
}
