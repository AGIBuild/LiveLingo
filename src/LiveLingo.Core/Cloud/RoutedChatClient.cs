using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Cloud;

/// <summary>Where the user sent each role: a cloud endpoint, or null for the local model.</summary>
public interface ICloudRoutes
{
    /// <summary>Read on every request, so a settings change applies immediately.</summary>
    Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct);
}

/// <summary>The default: every role runs on the local model.</summary>
public sealed class LocalOnlyRoutes : ICloudRoutes
{
    public Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct) => Task.FromResult<CloudRoute?>(null);
}

/// <summary>
/// The single chat client of a role. It follows the user's explicit choice: the cloud endpoint when one is
/// configured for the role, otherwise the local server. A cloud failure is reported; it is never retried locally.
/// </summary>
public sealed class RoutedChatClient(
    InferenceRole role,
    ICloudRoutes routes,
    IChatCompletionClient local,
    HttpClient http,
    ModelUsage? usage = null) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var route = await routes.GetAsync(role, ct).ConfigureAwait(false);
        if (route is null)
            return await local.CompleteAsync(request, ct).ConfigureAwait(false);

        var cloud = new OpenAiChatClient(http, route.Endpoint, route.Model, route.ApiKey, sendProviderExtras: false);
        usage?.Report(role, route.Model, cloud: true, confirmed: false);
        var completion = await cloud.CompleteAsync(request, ct).ConfigureAwait(false);
        if (completion.Model is { } served)
            usage?.Report(role, served, cloud: true, confirmed: true);
        return completion;
    }
}
