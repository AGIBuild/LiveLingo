using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;

namespace LiveLingo.Core.Cloud;

/// <summary>Where the user sent each role: a cloud endpoint, or null for the local model.</summary>
public interface ICloudRoutes
{
    /// <summary>Read on every request, so a settings change applies immediately.</summary>
    Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct);

    /// <summary>Returns the configured cloud speech route, or null when speech recognition runs locally.</summary>
    Task<SpeechCloudRoute?> GetSpeechAsync(CancellationToken ct) => Task.FromResult<SpeechCloudRoute?>(null);
}

/// <summary>The default: every role runs on the local model.</summary>
public sealed class LocalOnlyRoutes : ICloudRoutes
{
    public Task<CloudRoute?> GetAsync(InferenceRole role, CancellationToken ct) => Task.FromResult<CloudRoute?>(null);
}

/// <summary>
/// The single chat client of a role. It follows the user's explicit three-way choice:
/// <list type="number">
/// <item>the configured cloud endpoint,</item>
/// <item>the configured local model host (Bionic, LM Studio, Ollama, or local OpenAI-compatible server),</item>
/// <item>otherwise the built-in local llama-server.</item>
/// </list>
/// A cloud or local-host failure is reported explicitly; it is never silently retried on another backend.
/// </summary>
public sealed class RoutedChatClient(
    InferenceRole role,
    ICloudRoutes routes,
    IChatCompletionClient local,
    HttpClient http,
    ModelUsage? usage = null,
    ILocalHostRoutes? localHostRoutes = null,
    LocalHostRegistry? localHosts = null) : IChatCompletionClient
{
    public async Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var route = await routes.GetAsync(role, ct).ConfigureAwait(false);
        if (route is not null)
        {
            var cloud = new OpenAiChatClient(http, route.Endpoint, route.Model, route.ApiKey, sendProviderExtras: false);
            usage?.Report(role, route.Model, cloud: true, confirmed: false, endpoint: route.Name);
            var completion = await cloud.CompleteAsync(request, ct).ConfigureAwait(false);
            if (completion.Model is { } served)
                usage?.Report(role, served, cloud: true, confirmed: true, endpoint: route.Name);
            return completion;
        }

        if (localHostRoutes is not null && await localHostRoutes.GetAsync(role, ct).ConfigureAwait(false) is { } hostRoute)
        {
            if (localHosts is null)
            {
                throw new LocalHostUnavailableException(
                    LocalHostFailureKind.HostError,
                    hostRoute.HostId,
                    hostRoute.HostDisplayName,
                    $"Local host registry is not available for {hostRoute.HostDisplayName}.");
            }

            var hostClient = localHosts.CreateChatClient(hostRoute, http, usage);
            usage?.ReportLocalHost(role, hostRoute.ModelKey, confirmed: false, hostRoute.HostDisplayName);
            var completion = await hostClient.CompleteAsync(request, ct).ConfigureAwait(false);
            usage?.ReportLocalHost(role, completion.Model ?? hostRoute.ModelKey, confirmed: true, hostRoute.HostDisplayName);
            return completion;
        }

        return await local.CompleteAsync(request, ct).ConfigureAwait(false);
    }

    public async Task WarmPromptAsync(
        string systemPrompt,
        IReadOnlyDictionary<string, object>? extra = null,
        CancellationToken ct = default,
        string userPrefix = "目标：")
    {
        if (await routes.GetAsync(role, ct).ConfigureAwait(false) is not null)
            return;

        if (localHostRoutes is not null && await localHostRoutes.GetAsync(role, ct).ConfigureAwait(false) is { } hostRoute)
        {
            if (localHosts is not null)
            {
                var hostClient = localHosts.CreateChatClient(hostRoute, http, usage);
                await hostClient.WarmPromptAsync(systemPrompt, extra, ct, userPrefix).ConfigureAwait(false);
            }
            return;
        }

        await local.WarmPromptAsync(systemPrompt, extra, ct, userPrefix).ConfigureAwait(false);
    }
}
