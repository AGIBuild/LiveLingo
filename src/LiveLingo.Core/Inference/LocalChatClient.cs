using LiveLingo.Core.Models;

namespace LiveLingo.Core.Inference;

/// <summary>Chat completions served by a local llama-server; the server is started, reused and unloaded by <see cref="LlamaServerHost"/>.</summary>
public sealed class LocalChatClient(
    LlamaServerHost host, InferenceRole role, Func<ModelDescriptor> model, ModelUsage? usage = null) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var selected = model();
        usage?.Report(role, selected.DisplayName, cloud: false, confirmed: false);
        using var lease = await host.AcquireAsync(role, selected, ct).ConfigureAwait(false);
        usage?.Report(role, lease.Model.DisplayName, cloud: false, confirmed: true);
        return await lease.Client.CompleteAsync(request, ct).ConfigureAwait(false);
    }
}
