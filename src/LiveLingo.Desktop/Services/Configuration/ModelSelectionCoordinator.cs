using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;

namespace LiveLingo.Desktop.Services.Configuration;

public sealed record DownloadTaskSnapshot(
    ModelDescriptor Model,
    ModelStage? Stage,
    ModelDownloadState State,
    bool Queued,
    bool PendingActivation);

public sealed record InstalledCatalogItem(
    ModelDescriptor Model,
    ModelStage? Stage,
    long SizeBytes,
    bool InUse,
    bool CanActivate,
    bool CanDelete);

/// <summary>
/// Coordinates stage model selection, hardware compatibility checks, sequential download queuing,
/// staged download-then-activate transitions and local storage inventory across Settings and background downloads.
/// </summary>
public sealed class ModelSelectionCoordinator : IDisposable
{
    private static readonly ModelStage[] Stages = [ModelStage.Speech, ModelStage.Cleaner, ModelStage.Translator];

    private readonly ISettingsService _settings;
    private readonly IModelManager _models;
    private readonly IModelDownloadCoordinator _downloads;
    private readonly object _gate = new();
    private readonly Dictionary<ModelStage, string> _pendingByStage = [];
    private readonly List<ModelDescriptor> _downloadQueue = [];
    private Dictionary<string, long> _installedSizes = new(StringComparer.Ordinal);
    private bool _drainingQueue;

    public ModelSelectionCoordinator(
        ISettingsService settings,
        IModelManager models,
        IModelDownloadCoordinator downloads,
        IHardwareProbe? hardwareProbe = null)
    {
        _settings = settings;
        _models = models;
        _downloads = downloads;
        Hardware = hardwareProbe?.Detect() ?? HardwareProfile.Baseline32GbAppleMetal;
        _downloads.StateChanged += OnDownloadStateChanged;
    }

    public HardwareProfile Hardware { get; }

    /// <summary>Raised on whichever thread observed a download, activation or deletion state change.</summary>
    public event Action? Changed;

    public ModelSelection ActiveSelection
    {
        get
        {
            var composer = _settings.Current.Composer;
            var resolved = composer.ResolveSelection(Hardware);
            return new ModelSelection(
                PreferInstalledStage(ModelStage.Speech, composer.SpeechModel, resolved.SpeechModelId),
                PreferInstalledStage(ModelStage.Cleaner, composer.CleanerModel, resolved.CleanerModelId),
                PreferInstalledStage(ModelStage.Translator, composer.TranslatorModel, resolved.TranslatorModelId));
        }
    }

    private string PreferInstalledStage(ModelStage stage, string? explicitId, string resolvedId)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
            return resolvedId;
        var resolved = ModelCatalog.FindOption(stage, resolvedId)!;
        if (IsInstalled(resolved))
            return resolvedId;
        foreach (var candidate in ModelCatalog.OptionsFor(stage))
        {
            if (IsInstalled(candidate) && ModelAdvisor.Evaluate(candidate, Hardware).IsSelectable)
                return candidate.Id;
        }
        return resolvedId;
    }

    public ModelSelection RecommendedSelection
    {
        get
        {
            var cloud = _settings.Current.Cloud;
            return ModelAdvisor.Recommend(Hardware, cloud.Clean, cloud.Translate, cloud.Speech);
        }
    }

    public bool IsStageInCloud(ModelStage stage) => stage switch
    {
        ModelStage.Speech => _settings.Current.Cloud.Speech,
        ModelStage.Cleaner => _settings.Current.Cloud.Clean,
        ModelStage.Translator => _settings.Current.Cloud.Translate,
        _ => false
    };

    private IEnumerable<ModelStage> LocalStages => Stages.Where(stage => !IsStageInCloud(stage));

    /// <summary>True when all locally routed stages match <see cref="RecommendedSelection"/> and are installed on disk.</summary>
    public bool IsRecommendedActive
    {
        get
        {
            var localStages = LocalStages.ToArray();
            lock (_gate)
            {
                if (localStages.Any(stage => _pendingByStage.ContainsKey(stage)))
                    return false;
            }

            var active = ActiveSelection;
            var recommended = RecommendedSelection;
            return localStages.All(stage =>
                active.ModelIdFor(stage) == recommended.ModelIdFor(stage)
                && IsInstalled(recommended.DescriptorFor(stage)));
        }
    }

    /// <summary>True when every locally routed stage not yet ready on <see cref="RecommendedSelection"/> is downloading or queued toward it.</summary>
    public bool IsRecommendedDownloading
    {
        get
        {
            var active = ActiveSelection;
            var recommended = RecommendedSelection;
            var anyWorking = false;

            foreach (var stage in LocalStages)
            {
                var target = recommended.DescriptorFor(stage);
                if (active.ModelIdFor(stage) == target.Id && IsInstalled(target))
                    continue;
                var working = IsWorkingToward(target);
                if (!working)
                    return false;
                anyWorking = true;
            }

            return anyWorking;
        }
    }

    public string StorageDirectory
    {
        get
        {
            var probe = _models.GetModelDirectory("probe");
            return string.IsNullOrWhiteSpace(probe) ? "" : Path.GetDirectoryName(probe) ?? probe;
        }
    }

    private bool HasPendingActivations
    {
        get
        {
            lock (_gate)
                return _pendingByStage.Count > 0;
        }
    }

    public void SyncInstalledSnapshot(IReadOnlyList<InstalledModel> installed)
    {
        lock (_gate)
        {
            _installedSizes = installed
                .GroupBy(m => m.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().SizeBytes, StringComparer.Ordinal);
        }
    }

    public long InstalledCatalogBytes
    {
        get
        {
            lock (_gate)
            {
                long total = 0;
                foreach (var model in ModelCatalog.All)
                {
                    if (_installedSizes.TryGetValue(model.Id, out var diskBytes) && diskBytes > 0)
                        total += diskBytes;
                    else if (IsArtifactInstalledUnsafe(model))
                        total += model.SizeBytes;
                }
                return total;
            }
        }
    }

    /// <summary>Every catalog model or shared runtime/speech component currently installed on disk.</summary>
    public IReadOnlyList<InstalledCatalogItem> GetInstalledInventory()
    {
        var active = ActiveSelection;
        var cloud = _settings.Current.Cloud;
        var activeLocalArtifactIds = ActiveLocalArtifactIds(active, cloud);

        var items = new List<InstalledCatalogItem>();
        lock (_gate)
        {
            foreach (var model in ModelCatalog.All)
            {
                long sizeBytes;
                if (_installedSizes.TryGetValue(model.Id, out var recorded) && recorded > 0)
                    sizeBytes = recorded;
                else if (IsArtifactInstalledUnsafe(model))
                    sizeBytes = model.SizeBytes;
                else
                    continue;

                var stage = model.Profile?.Stage;
                var isSharedStageArtifact = ModelCatalog.IsSharedStageArtifact(model.Id);
                var downloading = _downloads.GetState(model.Id).IsDownloading;
                var inUse = stage is not null || isSharedStageArtifact
                    ? activeLocalArtifactIds.Contains(model.Id)
                    : ModelCatalog.IsSpeechAsset(model.Id)
                        ? !cloud.Speech
                        : !cloud.Clean || !cloud.Translate;
                var canActivate = stage is not null && !inUse && ModelAdvisor.Evaluate(model, Hardware, active, cloud.Clean, cloud.Translate, cloud.Speech).IsSelectable;
                var canDelete = (stage is not null || isSharedStageArtifact) && !inUse && !downloading && !IsQueuedUnsafe(model.Id);

                items.Add(new InstalledCatalogItem(model, stage, sizeBytes, inUse, canActivate, canDelete));
            }
        }

        // Selectable/shared stage models first (largest first so disk hogs stand out), then foundational runtime components.
        return
        [
            .. items
                .OrderBy(item => item.Stage is null && !ModelCatalog.IsSharedStageArtifact(item.Model.Id) ? 1 : 0)
                .ThenByDescending(item => item.SizeBytes)
        ];
    }

    /// <summary>All in-flight, queued or failed downloads across stage models and shared foundational components.</summary>
    public IReadOnlyList<DownloadTaskSnapshot> GetActiveDownloads()
    {
        HashSet<string> queuedIds;
        HashSet<string> pendingArtifactIds;
        lock (_gate)
        {
            queuedIds = _downloadQueue.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            pendingArtifactIds = _pendingByStage.Values
                .Select(id => ModelCatalog.FindSelectable(id)?.ArtifactId ?? id)
                .ToHashSet(StringComparer.Ordinal);
        }

        var list = new List<DownloadTaskSnapshot>();
        foreach (var model in ModelCatalog.All)
        {
            var state = _downloads.GetState(model.Id);
            var isQueued = !state.IsDownloading && !state.IsInstalled && queuedIds.Contains(model.Id);
            if (!state.IsDownloading && !state.IsFailed && !isQueued)
                continue;

            var effectiveState = isQueued
                ? new ModelDownloadState(model.Id, ModelDownloadStatus.Downloading, 0, null, 0, model.SizeBytes)
                : state.TotalBytes > 0
                    ? state
                    : state with { TotalBytes = model.SizeBytes };

            list.Add(new DownloadTaskSnapshot(
                model,
                model.Profile?.Stage,
                effectiveState,
                Queued: isQueued,
                PendingActivation: pendingArtifactIds.Contains(model.Id)));
        }

        return
        [
            .. list
                .OrderBy(item => item.State.IsDownloading && !item.Queued ? 0 : item.Queued ? 1 : 2)
        ];
    }

    public string? PendingModelId(ModelStage stage)
    {
        lock (_gate)
            return _pendingByStage.TryGetValue(stage, out var id) ? id : null;
    }

    public bool IsQueued(string modelId)
    {
        var artifactId = ModelCatalog.FindSelectable(modelId)?.ArtifactId ?? modelId;
        lock (_gate)
            return IsQueuedUnsafe(artifactId);
    }

    private bool IsQueuedUnsafe(string artifactId) =>
        _downloadQueue.Any(d => d.Id == artifactId) && !_downloads.GetState(artifactId).IsDownloading;

    public bool IsInstalled(ModelDescriptor descriptor)
    {
        lock (_gate)
            return IsArtifactInstalledUnsafe(descriptor);
    }

    public bool CanDeleteModel(string modelId)
    {
        if (ModelCatalog.FindAny(modelId) is not { } descriptor)
            return false;
        if (descriptor.Profile is null && !ModelCatalog.IsSharedStageArtifact(descriptor.Id))
            return false;

        var artifactId = descriptor.ArtifactId;
        if (_downloads.GetState(artifactId).IsDownloading || IsQueued(artifactId))
            return false;

        return !ActiveLocalArtifactIds(ActiveSelection, _settings.Current.Cloud).Contains(artifactId);
    }

    private static HashSet<string> ActiveLocalArtifactIds(ModelSelection active, CloudSettings cloud)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!cloud.Speech)
            ids.Add(active.Speech.ArtifactId);
        if (!cloud.Clean)
            ids.Add(active.Cleaner.ArtifactId);
        if (!cloud.Translate)
            ids.Add(active.Translator.ArtifactId);
        return ids;
    }

    public ModelDownloadState GetOptionDownloadState(ModelDescriptor option)
    {
        var artifact = option.ResolveArtifact();
        var primary = _downloads.GetState(artifact.Id);
        if (primary.IsDownloading || primary.IsFailed || primary.Status == ModelDownloadStatus.Cancelled)
            return primary with { ModelId = option.Id, TotalBytes = primary.TotalBytes > 0 ? primary.TotalBytes : option.SizeBytes };

        if (IsQueued(artifact.Id))
            return new ModelDownloadState(option.Id, ModelDownloadStatus.Downloading, 0, null, 0, option.SizeBytes);

        foreach (var companion in ModelCatalog.BundleFor(option))
        {
            if (companion.Id == artifact.Id)
                continue;
            var state = _downloads.GetState(companion.Id);
            if (state.IsDownloading || state.IsFailed)
                return new ModelDownloadState(option.Id, state.Status, state.Percentage, state.ErrorMessage, state.BytesDownloaded, state.TotalBytes);
        }

        return IsInstalled(artifact)
            ? new ModelDownloadState(option.Id, ModelDownloadStatus.Installed, 100, null, option.SizeBytes, option.SizeBytes)
            : primary with { ModelId = option.Id };
    }

    public void ClearPendingActivation(ModelStage stage)
    {
        bool removed;
        lock (_gate)
            removed = _pendingByStage.Remove(stage);
        if (removed)
            Changed?.Invoke();
    }

    /// <summary>
    /// Bytes that must be downloaded to make <see cref="RecommendedSelection"/> fully installed and active across local stages.
    /// </summary>
    public long MissingBytesForRecommended()
    {
        var recommended = RecommendedSelection;
        var needed = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var stage in LocalStages)
        {
            var recommendedOption = recommended.DescriptorFor(stage);
            if (IsInstalled(recommendedOption) || IsWorkingToward(recommendedOption))
                continue;

            foreach (var artifact in ModelCatalog.BundleFor(recommendedOption))
            {
                if (!IsInstalled(artifact) && !IsWorkingToward(artifact))
                    needed[artifact.Id] = artifact.SizeBytes;
            }
        }

        return needed.Values.Sum();
    }

    /// <summary>
    /// Immediately switches <paramref name="modelId"/>'s stage to run locally on <paramref name="modelId"/> when the model
    /// is selectable and either already on disk or its stage currently has no working backend (neither installed local nor cloud).
    /// </summary>
    public bool SelectModel(string modelId)
    {
        if (ModelCatalog.FindSelectable(modelId) is not { Profile: { } profile } option)
            return false;

        var active = ActiveSelection;
        var cloud = _settings.Current.Cloud;
        if (!ModelAdvisor.Evaluate(option, Hardware, active, cloud.Clean, cloud.Translate, cloud.Speech).IsSelectable)
            return false;

        var currentOption = active.DescriptorFor(profile.Stage);
        var hasWorkingCurrent = IsStageInCloud(profile.Stage) || IsInstalled(currentOption);
        if (!IsInstalled(option) && hasWorkingCurrent)
            return false;

        lock (_gate)
            _pendingByStage.Remove(profile.Stage);

        PersistStage(profile.Stage, option.Id);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Enqueues <paramref name="modelId"/> (and any missing companion runtime/speech assets) for sequential download,
    /// keeping any currently active local or cloud backend serving until the download finishes.
    /// </summary>
    public bool DownloadAndActivate(string modelId)
    {
        if (ModelCatalog.FindSelectable(modelId) is not { Profile: { } profile } option)
            return false;

        var active = ActiveSelection;
        var cloud = _settings.Current.Cloud;
        if (!ModelAdvisor.Evaluate(option, Hardware, active, cloud.Clean, cloud.Translate, cloud.Speech).IsSelectable)
            return false;

        var missing = ModelCatalog.BundleFor(option).Where(artifact => !IsInstalled(artifact)).ToList();
        if (missing.Count == 0)
        {
            lock (_gate)
                _pendingByStage.Remove(profile.Stage);
            PersistStage(profile.Stage, option.Id);
            Changed?.Invoke();
            return true;
        }

        var currentOption = active.DescriptorFor(profile.Stage);
        var hasWorkingCurrent = IsStageInCloud(profile.Stage) || IsInstalled(currentOption);
        if (!hasWorkingCurrent || IsInstalled(option))
            PersistStage(profile.Stage, option.Id);

        lock (_gate)
        {
            if (!IsInstalled(option))
                _pendingByStage[profile.Stage] = option.Id;

            foreach (var artifact in missing)
            {
                if (_downloadQueue.All(queued => queued.Id != artifact.Id) && !_downloads.GetState(artifact.Id).IsDownloading)
                    _downloadQueue.Add(artifact);
            }
        }

        EnsureQueueDraining();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Retries or starts downloading any single catalog artifact (including failed companion runtimes).</summary>
    public bool RetryArtifactDownload(string modelId)
    {
        if (ModelCatalog.FindSelectable(modelId) is not null)
            return DownloadAndActivate(modelId);

        if (ModelCatalog.All.FirstOrDefault(m => m.Id == modelId) is not { } artifact || IsInstalled(artifact))
            return false;

        lock (_gate)
        {
            if (_downloadQueue.All(queued => queued.Id != artifact.Id) && !_downloads.GetState(artifact.Id).IsDownloading)
                _downloadQueue.Add(artifact);
        }

        EnsureQueueDraining();
        Changed?.Invoke();
        return true;
    }

    public void CancelDownload(string modelId)
    {
        var descriptor = ModelCatalog.FindAny(modelId);
        if (descriptor is null)
            return;

        var artifactId = descriptor.ArtifactId;
        lock (_gate)
        {
            _downloadQueue.RemoveAll(d => d.Id == artifactId);
            foreach (var (stage, pendingId) in _pendingByStage.ToList())
            {
                var pendingArtifactId = ModelCatalog.FindOption(stage, pendingId)?.ArtifactId ?? pendingId;
                if (string.Equals(pendingArtifactId, artifactId, StringComparison.Ordinal))
                    _pendingByStage.Remove(stage);
            }
        }

        _downloads.Cancel(artifactId);
        Changed?.Invoke();
    }

    public async Task<bool> DeleteModelAsync(string modelId)
    {
        if (!CanDeleteModel(modelId) || ModelCatalog.FindAny(modelId) is not { } descriptor)
            return false;

        var artifactId = descriptor.ArtifactId;
        lock (_gate)
        {
            foreach (var (stage, pendingId) in _pendingByStage.ToList())
            {
                var pendingArtifactId = ModelCatalog.FindOption(stage, pendingId)?.ArtifactId ?? pendingId;
                if (string.Equals(pendingArtifactId, artifactId, StringComparison.Ordinal))
                    _pendingByStage.Remove(stage);
            }
            _downloadQueue.RemoveAll(d => d.Id == artifactId);
            _installedSizes.Remove(artifactId);
        }

        await _models.CleanObsoleteModelsAsync([artifactId]).ConfigureAwait(false);
        _downloads.NotifyDeleted(artifactId);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Applies <see cref="RecommendedSelection"/> across all locally routed stages without overriding any stage
    /// the user explicitly routed to the cloud.
    /// </summary>
    public void ApplyRecommended()
    {
        var active = ActiveSelection;
        var recommended = RecommendedSelection;
        var immediateUpdates = new List<(ModelStage Stage, string ModelId)>();
        var stagedDownloads = new List<ModelDescriptor>();

        foreach (var stage in LocalStages.ToArray())
        {
            var currentOption = active.DescriptorFor(stage);
            var recommendedOption = recommended.DescriptorFor(stage);
            var recommendedBundleInstalled = ModelCatalog.BundleFor(recommendedOption).All(IsInstalled);

            if (recommendedBundleInstalled)
            {
                lock (_gate)
                    _pendingByStage.Remove(stage);
                immediateUpdates.Add((stage, recommendedOption.Id));
            }
            else
            {
                if (!IsInstalled(currentOption))
                    immediateUpdates.Add((stage, recommendedOption.Id));
                stagedDownloads.Add(recommendedOption);
            }
        }

        if (immediateUpdates.Count > 0)
        {
            var model = _settings.CloneCurrent();
            model.Composer.SpeechModel = active.SpeechModelId;
            model.Composer.CleanerModel = active.CleanerModelId;
            model.Composer.TranslatorModel = active.TranslatorModelId;
            foreach (var (stage, id) in immediateUpdates)
                SetStageOnComposer(model.Composer, stage, id);
            _settings.Replace(model);
        }

        foreach (var download in stagedDownloads)
            DownloadAndActivate(download.Id);

        Changed?.Invoke();
    }

    public void Dispose() => _downloads.StateChanged -= OnDownloadStateChanged;

    private bool IsWorkingToward(ModelDescriptor descriptor)
    {
        var artifactId = descriptor.ArtifactId;
        return _downloads.GetState(artifactId).IsDownloading || IsQueued(artifactId);
    }

    private void EnsureQueueDraining()
    {
        lock (_gate)
        {
            if (_drainingQueue || _downloadQueue.Count == 0)
                return;
            _drainingQueue = true;
        }

        _ = DrainQueueAsync();
    }

    private async Task DrainQueueAsync()
    {
        while (true)
        {
            ModelDescriptor? next;
            lock (_gate)
            {
                if (_downloadQueue.Count == 0)
                {
                    _drainingQueue = false;
                    break;
                }
                next = _downloadQueue[0];
            }

            Changed?.Invoke();

            if (!IsInstalled(next))
                await _downloads.StartAsync(next).ConfigureAwait(false);

            lock (_gate)
                _downloadQueue.RemoveAll(d => d.Id == next.Id);

            Changed?.Invoke();
        }
    }

    private bool IsArtifactInstalledUnsafe(ModelDescriptor descriptor)
    {
        var artifact = descriptor.ResolveArtifact();
        return _installedSizes.ContainsKey(artifact.Id)
            || _models.HasAllExpectedLocalAssets(artifact)
            || _downloads.GetState(artifact.Id).IsInstalled;
    }

    private void OnDownloadStateChanged(ModelDownloadState state)
    {
        List<(ModelStage Stage, string ModelId)> ready = [];

        lock (_gate)
        {
            if (state.IsInstalled && ModelCatalog.All.FirstOrDefault(m => m.Id == state.ModelId) is { } installed)
                _installedSizes.TryAdd(installed.Id, installed.SizeBytes);

            foreach (var (stage, pendingId) in _pendingByStage.ToList())
            {
                if (ModelCatalog.FindOption(stage, pendingId) is not { } pendingOption)
                {
                    _pendingByStage.Remove(stage);
                    continue;
                }

                var bundle = ModelCatalog.BundleFor(pendingOption);
                var bundleReady = bundle.All(d =>
                    d.Id == state.ModelId
                        ? state.IsInstalled
                        : d.Id == pendingOption.ArtifactId
                            ? IsArtifactInstalledUnsafe(d)
                            : IsCompanionDoneUnsafe(d));

                if (bundleReady)
                {
                    _pendingByStage.Remove(stage);
                    ready.Add((stage, pendingId));
                }
                else if (bundle.Any(d => d.Id == state.ModelId) && state.Status is ModelDownloadStatus.Cancelled or ModelDownloadStatus.Failed)
                {
                    _pendingByStage.Remove(stage);
                }
            }
        }

        foreach (var (stage, modelId) in ready)
            PersistStage(stage, modelId);

        Changed?.Invoke();
    }

    private bool IsCompanionDoneUnsafe(ModelDescriptor companion)
    {
        if (IsArtifactInstalledUnsafe(companion))
            return true;
        if (_downloadQueue.Any(d => d.Id == companion.Id))
            return false;
        var companionState = _downloads.GetState(companion.Id);
        return !companionState.IsDownloading && !companionState.IsFailed && companionState.Status != ModelDownloadStatus.Cancelled;
    }

    private void PersistStage(ModelStage stage, string modelId)
    {
        var active = ActiveSelection;
        var model = _settings.CloneCurrent();
        model.Composer.SpeechModel = active.SpeechModelId;
        model.Composer.CleanerModel = active.CleanerModelId;
        model.Composer.TranslatorModel = active.TranslatorModelId;
        SetStageOnComposer(model.Composer, stage, modelId);
        ClearCloudOnStage(model.Cloud, stage);
        _settings.Replace(model);
    }

    private static void ClearCloudOnStage(CloudSettings cloud, ModelStage stage)
    {
        switch (stage)
        {
            case ModelStage.Speech: cloud.SpeechEndpointId = ""; break;
            case ModelStage.Cleaner: cloud.CleanerEndpointId = ""; break;
            case ModelStage.Translator: cloud.TranslatorEndpointId = ""; break;
        }
    }

    private static void SetStageOnComposer(ComposerSettings composer, ModelStage stage, string modelId)
    {
        switch (stage)
        {
            case ModelStage.Speech: composer.SpeechModel = modelId; break;
            case ModelStage.Cleaner: composer.CleanerModel = modelId; break;
            case ModelStage.Translator: composer.TranslatorModel = modelId; break;
            default: throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }
}
