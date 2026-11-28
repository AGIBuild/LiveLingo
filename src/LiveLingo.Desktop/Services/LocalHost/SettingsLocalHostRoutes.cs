using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Services.LocalHost;

/// <summary>
/// Resolves <see cref="LocalHostRoute"/> dynamically from <see cref="LocalHostSettings"/>, <see cref="LocalHostCoordinator"/>
/// live discovery metadata, and <see cref="ISecretStore"/>. An unreachable or missing bound host raises an explicit
/// <see cref="LocalHostUnavailableException"/>; traffic never silently falls back to another backend.
/// </summary>
public sealed class SettingsLocalHostRoutes(
    ISettingsService settings,
    ISecretStore secrets,
    LocalHostCoordinator coordinator) : ILocalHostRoutes
{
    public Task<LocalHostRoute?> GetAsync(InferenceRole role, CancellationToken ct)
    {
        var localHost = settings.Current.LocalHost;
        var binding = role switch
        {
            InferenceRole.Cleaner => localHost.Cleaner,
            InferenceRole.Translator => localHost.Translator,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        return ResolveBindingAsync(binding, ct);
    }

    public async Task<LocalHostRoute?> GetSpeechAsync(CancellationToken ct)
    {
        var binding = settings.Current.LocalHost.Speech;
        if (!binding.IsBound)
            return null;

        var knownKind = coordinator.FindHost(binding.HostId)?.Kind ?? InferKind(binding.HostId);
        if (!LocalHostRoute.SupportsHttpSpeech(knownKind))
        {
            var displayName = coordinator.FindHost(binding.HostId)?.DisplayName ?? DisplayNameFor(binding.HostId, knownKind);
            throw UnsupportedSpeechHost(binding.HostId, displayName);
        }

        var route = await ResolveBindingAsync(binding, ct).ConfigureAwait(false);
        if (route is not null && !LocalHostRoute.SupportsHttpSpeech(route.Kind))
            throw UnsupportedSpeechHost(route.HostId, route.HostDisplayName);

        return route;
    }

    private static LocalHostUnavailableException UnsupportedSpeechHost(string hostId, string displayName) => new(
        LocalHostFailureKind.HostError,
        hostId,
        displayName,
        $"{displayName} does not expose an HTTP audio transcription API. Switch Speech to a built-in model or an OpenAI-compatible local ASR server.");

    private async Task<LocalHostRoute?> ResolveBindingAsync(LocalHostStageBinding binding, CancellationToken ct)
    {
        if (!binding.IsBound)
            return null;

        var host = coordinator.FindHost(binding.HostId);
        if (host is null || host.Status != LocalHostStatus.Running)
        {
            await coordinator.RefreshAsync(force: host is null, ct).ConfigureAwait(false);
            host = coordinator.FindHost(binding.HostId);
        }

        var token = await SafeGetSecretAsync(LocalHostSettings.TokenSecretIdFor(binding.HostId), ct).ConfigureAwait(false);

        if (host is not null)
        {
            if (host.Status == LocalHostStatus.AuthRequired)
            {
                throw new LocalHostUnavailableException(
                    LocalHostFailureKind.AuthRequired, host.HostId, host.DisplayName,
                    $"{host.DisplayName} requires an API token.");
            }

            if (host.Status != LocalHostStatus.Running || host.BaseUri is null)
            {
                throw new LocalHostUnavailableException(
                    LocalHostFailureKind.ServerNotRunning, host.HostId, host.DisplayName,
                    $"{host.DisplayName} local API server is not running.");
            }

            var model = host.Models.FirstOrDefault(m => string.Equals(m.Key, binding.ModelKey, StringComparison.OrdinalIgnoreCase));
            if (model is null)
            {
                throw new LocalHostUnavailableException(
                    LocalHostFailureKind.ModelNotFound, host.HostId, host.DisplayName,
                    $"Model '{binding.ModelKey}' was not found in {host.DisplayName}.");
            }

            return LocalHostRoute.Create(
                host.HostId,
                host.DisplayName,
                host.Kind,
                host.BaseUri.ToString(),
                model.Key,
                host.ApiTier,
                reasoningOffSupported: model.Reasoning?.SupportsOff == true,
                isLoaded: model.IsLoaded,
                parallelSlots: model.ParallelSlots,
                bearerToken: token);
        }

        // Host not in discovery list (for example a stale binding): try persisted loopback URL so the client surfaces a precise failure.
        if (!LocalHostRoute.TryParseLoopbackUri(binding.BaseUrl, out _))
        {
            throw new LocalHostUnavailableException(
                LocalHostFailureKind.ServerNotRunning,
                binding.HostId,
                binding.HostId,
                $"Local model host '{binding.HostId}' is not reachable.");
        }

        var fallbackKind = InferKind(binding.HostId);
        var fallbackTier = fallbackKind switch
        {
            LocalHostKind.Bionic or LocalHostKind.LmStudio => LocalHostApiTier.NativeV1,
            LocalHostKind.Ollama => LocalHostApiTier.OllamaNative,
            _ => LocalHostApiTier.OpenAiCompat
        };
        return LocalHostRoute.Create(
            binding.HostId,
            DisplayNameFor(binding.HostId, fallbackKind),
            fallbackKind,
            binding.BaseUrl,
            binding.ModelKey,
            fallbackTier,
            bearerToken: token);
    }

    private async Task<string?> SafeGetSecretAsync(string secretId, CancellationToken ct)
    {
        try
        {
            return await secrets.GetSecretAsync(secretId, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static LocalHostKind InferKind(string hostId) => hostId.ToLowerInvariant() switch
    {
        LocalHostEnvironmentProbe.BionicHostId => LocalHostKind.Bionic,
        LocalHostEnvironmentProbe.LmStudioHostId => LocalHostKind.LmStudio,
        LocalHostEnvironmentProbe.OllamaHostId => LocalHostKind.Ollama,
        _ => LocalHostKind.OpenAiLocal
    };

    private static string DisplayNameFor(string hostId, LocalHostKind kind) => kind switch
    {
        LocalHostKind.Bionic => "Bionic",
        LocalHostKind.LmStudio => "LM Studio",
        LocalHostKind.Ollama => "Ollama",
        _ => hostId
    };
}
