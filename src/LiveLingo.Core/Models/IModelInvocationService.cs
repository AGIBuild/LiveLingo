namespace LiveLingo.Core.Models;

public interface IModelInvocationService
{
    Task<ModelInvocationResult> InvokeAsync(
        ModelInvocationRequest request,
        CancellationToken ct = default);
}
