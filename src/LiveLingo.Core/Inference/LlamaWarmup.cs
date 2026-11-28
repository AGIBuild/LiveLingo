using LiveLingo.Core.Cloud;
using LiveLingo.Core.LocalHost;

namespace LiveLingo.Core.Inference;

public sealed class LlamaWarmup(
    LlamaServerHost host,
    ICloudRoutes routes,
    CoreOptions options,
    ILocalHostRoutes? localHostRoutes = null,
    LocalHostRegistry? localHosts = null) : IInferenceWarmup
{
    public async Task WarmAsync(InferenceRole role, CancellationToken ct = default)
    {
        if (await routes.GetAsync(role, ct).ConfigureAwait(false) is not null)
            return;

        if (localHostRoutes is not null && await localHostRoutes.GetAsync(role, ct).ConfigureAwait(false) is { } hostRoute)
        {
            if (localHosts is not null)
                await localHosts.TryWarmLoadAsync(hostRoute, ct).ConfigureAwait(false);
            return;
        }

        await host.WarmAsync(role, InferenceModels.For(role, options.Models), ct).ConfigureAwait(false);
    }
}
