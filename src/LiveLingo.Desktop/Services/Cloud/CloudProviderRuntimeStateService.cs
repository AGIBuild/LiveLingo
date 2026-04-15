using System.Text.Json;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace LiveLingo.Desktop.Services.Cloud;

public sealed class CloudProviderRuntimeStateService : ICloudProviderRuntimeState
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ICloudProviderProbeService _probeService;
    private readonly ILogger<CloudProviderRuntimeStateService> _logger;
    private readonly string _cachePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _loadGate = new();
    private CloudProviderRuntimeSnapshot _current = CloudProviderRuntimeSnapshot.Unknown;
    private bool _cacheLoaded;

    public CloudProviderRuntimeStateService(
        ICloudProviderProbeService probeService,
        ILogger<CloudProviderRuntimeStateService> logger,
        string? cachePath = null,
        Func<DateTimeOffset>? clock = null)
    {
        _probeService = probeService;
        _logger = logger;
        _cachePath = cachePath ?? GetDefaultCachePath();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public CloudProviderRuntimeSnapshot Current
    {
        get
        {
            EnsureCacheLoaded();
            return _current;
        }
    }

    public event Action<CloudProviderRuntimeSnapshot>? Changed;

    public CloudProviderRoutingState GetRoutingState(CloudModelPreferences? preferences)
    {
        EnsureCacheLoaded();
        if (preferences is not { Enabled: true })
            return CloudProviderRoutingState.Unknown;

        if (!_current.Matches(preferences))
            return CloudProviderRoutingState.Unknown;

        if (_current.IsExpired(_clock()))
            return CloudProviderRoutingState.Unknown;

        return new CloudProviderRoutingState(
            HasValidation: _current.Status != CloudProviderRuntimeStatus.Unknown,
            IsHealthy: _current.Status == CloudProviderRuntimeStatus.Healthy,
            Message: _current.Status == CloudProviderRuntimeStatus.Healthy ? null : _current.Message,
            AvailableModelIds: _current.Models
                .Select(model => model.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public async Task<CloudProviderRuntimeSnapshot> RefreshAsync(CloudModelPreferences? preferences, CancellationToken ct = default)
    {
        EnsureCacheLoaded();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (preferences is not { Enabled: true })
            {
                return UpdateCurrent(CloudProviderRuntimeSnapshot.Unknown, persist: true);
            }

            var fingerprint = CloudProviderConfigurationFingerprint.Create(preferences);
            var now = _clock();
            if (!IsConfigurationComplete(preferences))
            {
                return await PersistAndUpdateAsync(
                    new CloudProviderRuntimeSnapshot(
                        fingerprint,
                        CloudProviderRuntimeStatus.InvalidConfiguration,
                        CloudProviderValidationMode.Unknown,
                        "Cloud provider configuration is incomplete. Set base URL, API key, and a translation model in Settings.",
                        now,
                        now.AddMinutes(5),
                        []),
                    ct).ConfigureAwait(false);
            }

            var previousModels = _current.Matches(preferences) ? _current.Models : [];
            try
            {
                var probeRequest = new CloudProviderProbeRequest(
                    preferences.BaseUrl!.Trim(),
                    preferences.ApiKey!.Trim(),
                    preferences.TranslationModelId,
                    preferences.PostProcessingModelId);
                var catalog = await _probeService.GetModelCatalogAsync(
                    probeRequest,
                    ct).ConfigureAwait(false);
                if (!catalog.IsSupported)
                {
                    try
                    {
                        await _probeService.ProbeModelAsync(probeRequest, preferences.TranslationModelId!, ct).ConfigureAwait(false);
                        return await PersistAndUpdateAsync(
                            new CloudProviderRuntimeSnapshot(
                                fingerprint,
                                CloudProviderRuntimeStatus.Healthy,
                                CloudProviderValidationMode.DirectModelProbe,
                                $"Connection succeeded. Provider does not expose a model catalog; validated translation model '{preferences.TranslationModelId}' directly.",
                                now,
                                now.AddHours(24),
                                []),
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException or TimeoutRejectedException)
                    {
                        _logger.LogWarning(ex, "Cloud provider direct probe failed for {BaseUrl}", preferences.BaseUrl);
                        return await PersistAndUpdateAsync(
                            new CloudProviderRuntimeSnapshot(
                                fingerprint,
                                CloudProviderRuntimeStatus.Unavailable,
                                CloudProviderValidationMode.DirectModelProbe,
                                ex.Message,
                                now,
                                now.AddMinutes(5),
                                previousModels),
                            ct).ConfigureAwait(false);
                    }
                }

                var (status, message, validationMode, models) = EvaluateCatalogResult(preferences, catalog.Models);
                return await PersistAndUpdateAsync(
                    new CloudProviderRuntimeSnapshot(
                        fingerprint,
                        status,
                        validationMode,
                        message,
                        now,
                        status == CloudProviderRuntimeStatus.Healthy ? now.AddHours(24) : now.AddMinutes(15),
                        models),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException or TimeoutRejectedException)
            {
                _logger.LogWarning(ex, "Cloud provider validation failed for {BaseUrl}", preferences.BaseUrl);
                return await PersistAndUpdateAsync(
                    new CloudProviderRuntimeSnapshot(
                        fingerprint,
                        CloudProviderRuntimeStatus.Unavailable,
                        CloudProviderValidationMode.Unknown,
                        ex.Message,
                        now,
                        now.AddMinutes(5),
                        previousModels),
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsConfigurationComplete(CloudModelPreferences preferences) =>
        !string.IsNullOrWhiteSpace(preferences.BaseUrl)
        && !string.IsNullOrWhiteSpace(preferences.ApiKey)
        && !string.IsNullOrWhiteSpace(preferences.TranslationModelId);

    private static (CloudProviderRuntimeStatus Status, string Message, CloudProviderValidationMode ValidationMode, IReadOnlyList<CloudProviderModelInfo> Models) EvaluateCatalogResult(
        CloudModelPreferences preferences,
        IReadOnlyList<CloudProviderModelInfo> models)
    {
        if (models.Count == 0)
        {
            return (
                CloudProviderRuntimeStatus.Unavailable,
                "Provider model catalog returned no models.",
                CloudProviderValidationMode.ModelCatalog,
                models);
        }

        var missing = new List<string>();
        if (!ContainsModel(models, preferences.TranslationModelId))
            missing.Add($"translation model '{preferences.TranslationModelId}'");

        var postProcessingModelId = preferences.ResolvePostProcessingModelId();
        if (!string.IsNullOrWhiteSpace(postProcessingModelId) &&
            !string.Equals(postProcessingModelId, preferences.TranslationModelId, StringComparison.OrdinalIgnoreCase) &&
            !ContainsModel(models, postProcessingModelId))
        {
            missing.Add($"post-processing model '{postProcessingModelId}'");
        }

        if (missing.Count > 0)
        {
            return (
                CloudProviderRuntimeStatus.InvalidModelSelection,
                $"Configured {string.Join(" and ", missing)} was not found in the provider model list.",
                CloudProviderValidationMode.ModelCatalog,
                models);
        }

        return (
            CloudProviderRuntimeStatus.Healthy,
            models.Count > 0
                ? $"Connection succeeded. {models.Count} models available."
                : "Connection succeeded, but the provider returned no models.",
            CloudProviderValidationMode.ModelCatalog,
            models);
    }

    private static bool ContainsModel(IReadOnlyList<CloudProviderModelInfo> models, string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && models.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));

    private async Task<CloudProviderRuntimeSnapshot> PersistAndUpdateAsync(
        CloudProviderRuntimeSnapshot snapshot,
        CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_cachePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = $"{_cachePath}.tmp";
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, _cachePath, true);
        return UpdateCurrent(snapshot, persist: false);
    }

    private CloudProviderRuntimeSnapshot UpdateCurrent(CloudProviderRuntimeSnapshot snapshot, bool persist)
    {
        _current = snapshot;
        if (persist)
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_cachePath, json);
        }

        Changed?.Invoke(snapshot);
        return snapshot;
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheLoaded)
            return;

        lock (_loadGate)
        {
            if (_cacheLoaded)
                return;

            if (File.Exists(_cachePath))
            {
                try
                {
                    var json = File.ReadAllText(_cachePath);
                    _current = JsonSerializer.Deserialize<CloudProviderRuntimeSnapshot>(json, JsonOptions)
                        ?? CloudProviderRuntimeSnapshot.Unknown;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    _logger.LogWarning(ex, "Failed to load cloud provider runtime cache from {Path}", _cachePath);
                    _current = CloudProviderRuntimeSnapshot.Unknown;
                }
            }

            _cacheLoaded = true;
        }
    }

    private static string GetDefaultCachePath()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "LiveLingo")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveLingo");

        return Path.Combine(root, "cloud-provider-state.json");
    }
}
