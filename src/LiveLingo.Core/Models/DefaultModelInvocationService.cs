using System.Runtime.CompilerServices;

namespace LiveLingo.Core.Models;

public sealed class DefaultModelInvocationService : IModelInvocationService
{
    private readonly IReadOnlyList<IModelRuntime> _runtimes;
    private readonly IReadOnlyList<IModelProvider> _providers;

    public DefaultModelInvocationService(
        IEnumerable<IModelRuntime> runtimes,
        IEnumerable<IModelProvider> providers)
    {
        _runtimes = runtimes.ToArray();
        _providers = providers.ToArray();
    }

    public async Task<ModelInvocationResult> InvokeAsync(
        ModelInvocationRequest request,
        CancellationToken ct = default)
    {
        var (session, provider) = await ResolveAsync(request, ct).ConfigureAwait(false);
        return await provider.InvokeAsync(session, request, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> InvokeStreamingAsync(
        ModelInvocationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (session, provider) = await ResolveAsync(request, ct).ConfigureAwait(false);
        await foreach (var delta in provider.InvokeStreamingAsync(session, request, ct).ConfigureAwait(false))
            yield return delta;
    }

    private async Task<(ModelRuntimeSession Session, IModelProvider Provider)> ResolveAsync(
        ModelInvocationRequest request, CancellationToken ct)
    {
        var runtime = _runtimes.FirstOrDefault(r => r.RuntimeKind == request.Profile.RuntimeKind)
            ?? throw new InvalidOperationException(
                $"No model runtime registered for {request.Profile.RuntimeKind}.");

        var provider = _providers.FirstOrDefault(p => p.ProviderKind == request.Profile.ProviderKind)
            ?? throw new InvalidOperationException(
                $"No model provider registered for {request.Profile.ProviderKind}.");

        var session = await runtime.AcquireSessionAsync(request.Profile, request.TaskType, ct).ConfigureAwait(false);
        return (session, provider);
    }
}
