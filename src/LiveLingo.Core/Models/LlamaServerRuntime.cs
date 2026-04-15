using LiveLingo.Core.Processing;

namespace LiveLingo.Core.Models;

public sealed class LlamaServerRuntime(LocalLlamaModelHost host) : IModelRuntime
{
    public ModelRuntimeKind RuntimeKind => ModelRuntimeKind.LlamaServer;

    public async Task<ModelRuntimeSession> AcquireSessionAsync(
        ModelProfile profile,
        ModelTaskType taskType,
        CancellationToken ct = default)
    {
        var endpoint = await host.GetOrStartServerAsync(profile, ct).ConfigureAwait(false);
        return new ModelRuntimeSession(profile, taskType, endpoint);
    }
}
