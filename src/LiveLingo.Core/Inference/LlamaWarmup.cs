using LiveLingo.Core.Cloud;

namespace LiveLingo.Core.Inference;

public sealed class LlamaWarmup(LlamaServerHost host, ICloudRoutes routes, CoreOptions options) : IInferenceWarmup
{
    public async Task WarmAsync(InferenceRole role, CancellationToken ct = default)
    {
        if (await routes.GetAsync(role, ct).ConfigureAwait(false) is not null)
            return;

        await host.WarmAsync(role, InferenceModels.For(role, options.TranslatorTier), ct).ConfigureAwait(false);
    }
}
