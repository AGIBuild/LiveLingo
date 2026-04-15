using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Models;

public sealed class RemoteHttpRuntime(IOptions<CoreOptions> options) : IModelRuntime
{
    public ModelRuntimeKind RuntimeKind => ModelRuntimeKind.RemoteHttp;

    public Task<ModelRuntimeSession> AcquireSessionAsync(
        ModelProfile profile,
        ModelTaskType taskType,
        CancellationToken ct = default)
    {
        var baseUrl = options.Value.CloudProviderBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Cloud provider base URL is not configured.");

        return Task.FromResult(new ModelRuntimeSession(profile, taskType, baseUrl));
    }
}
