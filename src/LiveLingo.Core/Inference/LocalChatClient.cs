using LiveLingo.Core.Models;

namespace LiveLingo.Core.Inference;

/// <summary>Chat completions served by a local llama-server; the server is started, reused and unloaded by <see cref="LlamaServerHost"/>.</summary>
public sealed class LocalChatClient(LlamaServerHost host, InferenceRole role, Func<ModelDescriptor> model) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        using var lease = await host.AcquireAsync(role, model(), ct).ConfigureAwait(false);
        return await lease.Client.CompleteAsync(request, ct).ConfigureAwait(false);
    }
}
