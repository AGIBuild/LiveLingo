using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

/// <summary>
/// State and commands of the composer window: the editable source text, the translated result, the output
/// choice, banners and the write-back flow. It holds no UI types; the window binds to it and maps enums to text.
/// </summary>
public sealed partial class ComposerViewModel : ObservableObject, IDisposable
{
    private readonly DraftSession _session;
    private readonly TranslationScheduler _scheduler;
    private readonly IReadOnlyList<ModelDescriptor> _requiredModels;
    private readonly IModelManager _models;
    private readonly IModelDownloadCoordinator _downloads;
    private readonly IPlatformServices _platform;
    private readonly TargetWindowInfo? _target;
    private readonly SynchronizationContext? _notifyOn;
    private CommitSnapshot? _committed;

    public ComposerViewModel(
        DraftSession session,
        TranslationScheduler scheduler,
        IReadOnlyList<ModelDescriptor> requiredModels,
        IModelManager models,
        IModelDownloadCoordinator downloads,
        IPlatformServices platform,
        TargetWindowInfo? target,
        SynchronizationContext? notifyOn = null)
    {
        _session = session;
        _scheduler = scheduler;
        _requiredModels = requiredModels;
        _models = models;
        _downloads = downloads;
        _platform = platform;
        _target = target;
        _notifyOn = notifyOn;
        _targetLanguage = scheduler.Target;

        _modelsReady = AreModelsInstalled();
        ApplyDraftOutput();
        _session.Changed += OnDraftChanged;
        _downloads.StateChanged += OnDownloadStateChanged;
        Refresh();
    }

    /// <summary>Raised after the result was written (or copied when there is no target); the window should close.</summary>
    public event Action? Completed;

    /// <summary>Raised when the user dismisses the composer with nothing pending.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private ComposerOutput _output = ComposerOutput.Translate;
    [ObservableProperty] private TargetLanguage _targetLanguage;
    [ObservableProperty] private IReadOnlyList<ResultLine> _lines = [];
    [ObservableProperty] private ComposerStatus _status = ComposerStatus.Empty;
    [ObservableProperty] private BannerState _banner = BannerState.None;
    [ObservableProperty] private bool _isWriteQueued;
    [ObservableProperty] private bool _canWrite;
    [ObservableProperty] private bool _canCopy;
    [ObservableProperty] private bool _isEditable = true;
    [ObservableProperty] private bool _modelsReady;

    public bool HasTarget => _target is not null;
    public string TargetName => _target?.ProcessName ?? "";

    partial void OnSourceTextChanged(string value)
    {
        if (_committed is not null)
            return;

        CancelQueuedWrite();
        var route = TextUnits.RouteFor(value);
        var units = route == DraftRoute.Whole ? TextUnits.Whole(value) : TextUnits.Split(value);
        _session.Mutate(draft => draft.SyncSource(units, route));
    }

    partial void OnOutputChanged(ComposerOutput value)
    {
        CancelQueuedWrite();
        ApplyDraftOutput();
    }

    partial void OnTargetLanguageChanged(TargetLanguage value)
    {
        CancelQueuedWrite();
        _scheduler.SetTarget(value);
    }

    [RelayCommand]
    private void SwapLanguage() =>
        TargetLanguage = TargetLanguage == TargetLanguage.English ? TargetLanguage.Chinese : TargetLanguage.English;

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (IsWriteQueued || _committed is not null)
            return;
        if (Output == ComposerOutput.Translate && !ModelsReady)
            return;

        MarkTargetLost();
        var result = _session.Mutate(draft => draft.RequestCommit());
        if (result.Snapshot is { } snapshot)
        {
            await CompleteAsync(snapshot).ConfigureAwait(true);
            return;
        }

        switch (result.Blocked)
        {
            case BlockReason.Processing:
                IsWriteQueued = true;
                _scheduler.FlushTail();
                break;
            case BlockReason.TargetLost:
                Banner = new BannerState(BannerKind.TargetLost);
                break;
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (!CanCopy)
            return;

        var text = _committed?.Text ?? _session.Read(draft => draft.Text());
        await _platform.Clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsWriteQueued)
            CancelQueuedWrite();
        else
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Retry(int index) => _session.Mutate(draft => draft.Retry(index, UnitKind.Translation));

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        foreach (var model in _requiredModels.Where(model => !_models.HasAllExpectedLocalAssets(model)))
        {
            await _downloads.StartAsync(model).ConfigureAwait(true);
            if (!_models.HasAllExpectedLocalAssets(model))
            {
                var error = _downloads.GetState(model.Id).ErrorMessage ?? "";
                Banner = new BannerState(BannerKind.DownloadFailed, error);
                return;
            }
        }

        ModelsReady = AreModelsInstalled();
        Banner = BannerState.None;
        ApplyDraftOutput();
        RetryFailedTranslations();
    }

    public void Dispose()
    {
        _session.Changed -= OnDraftChanged;
        _downloads.StateChanged -= OnDownloadStateChanged;
    }

    private bool AreModelsInstalled() => _requiredModels.All(_models.HasAllExpectedLocalAssets);

    private void ApplyDraftOutput()
    {
        var output = Output == ComposerOutput.Translate && ModelsReady ? OutputKind.Translation : OutputKind.Raw;
        _session.Mutate(draft => draft.Output = output);
    }

    private void RetryFailedTranslations() =>
        _session.Mutate(draft =>
        {
            for (var i = 0; i < draft.Units.Count; i++)
            {
                if (draft.Units[i].Translation.Error.Length > 0)
                    draft.Retry(i, UnitKind.Translation);
            }
        });

    private void MarkTargetLost()
    {
        if (_target is not null && !_platform.WindowTracker.IsAlive(_target))
            _session.Mutate(draft => draft.TargetLost = true);
    }

    private void CancelQueuedWrite() => IsWriteQueued = false;

    private async Task CompleteAsync(CommitSnapshot snapshot)
    {
        _committed = snapshot;
        IsEditable = false;
        try
        {
            if (snapshot.Action == CommitAction.Insert)
                await _platform.TextInjector.InjectAsync(_target!, snapshot.Text, autoSend: false).ConfigureAwait(true);
            else
                await _platform.Clipboard.SetTextAsync(snapshot.Text).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsWriteQueued = false;
            Banner = new BannerState(BannerKind.WriteFailed, ex.Message);
            Refresh();
            return;
        }

        IsWriteQueued = false;
        Completed?.Invoke();
    }

    private void OnDraftChanged()
    {
        Refresh();
        if (IsWriteQueued && Status == ComposerStatus.Ready)
            _ = WriteQueuedAsync();
        else if (IsWriteQueued && Status == ComposerStatus.Failed)
            CancelQueuedWrite();
    }

    private async Task WriteQueuedAsync()
    {
        IsWriteQueued = false;
        await WriteAsync().ConfigureAwait(true);
    }

    private void OnDownloadStateChanged(ModelDownloadState state)
    {
        if (_requiredModels.All(model => model.Id != state.ModelId))
            return;

        void Apply()
        {
            if (state.Status == ModelDownloadStatus.Downloading)
                Banner = new BannerState(BannerKind.Downloading, "", OverallProgress());
        }

        if (_notifyOn is null)
            Apply();
        else
            _notifyOn.Post(_ => Apply(), null);
    }

    private double OverallProgress()
    {
        var total = _requiredModels.Sum(model => (double)model.SizeBytes);
        var done = _requiredModels.Sum(model => model.SizeBytes * _downloads.GetState(model.Id).Percentage / 100);
        return total > 0 ? done / total * 100 : 0;
    }

    private void Refresh()
    {
        var snapshot = _session.Read(draft => (
            Lines: BuildLines(draft),
            Content: draft.ContentReadiness(),
            Insertion: draft.InsertionReadiness(),
            Count: draft.Units.Count));

        Lines = snapshot.Lines;
        Status = StatusOf(snapshot.Count, snapshot.Content);
        CanCopy = _committed is not null || Status == ComposerStatus.Ready;
        CanWrite = CanCopy && _committed is null;

        if (!ModelsReady && Output == ComposerOutput.Translate && Banner.Kind is BannerKind.None)
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
    }

    private string MissingSizeLabel()
    {
        var bytes = _requiredModels.Where(model => !_models.HasAllExpectedLocalAssets(model)).Sum(model => model.SizeBytes);
        return $"{bytes / 1_000_000_000d:0.0} GB";
    }

    private IReadOnlyList<ResultLine> BuildLines(Draft draft)
    {
        var translate = Output == ComposerOutput.Translate;
        var lines = new List<ResultLine>(draft.Units.Count);
        for (var i = 0; i < draft.Units.Count; i++)
        {
            var unit = draft.Units[i];
            if (!translate)
            {
                lines.Add(new ResultLine(i, unit.Raw, LineState.Ready, ""));
                continue;
            }

            var state = ModelsReady
                ? draft.StateOf(unit, OutputKind.Translation) switch
                {
                    UnitState.Ready => LineState.Ready,
                    UnitState.Error => LineState.Failed,
                    UnitState.Empty => LineState.Empty,
                    _ => LineState.Pending
                }
                : LineState.Pending;
            lines.Add(new ResultLine(i, unit.Translation.Text ?? "", state, unit.Translation.Error));
        }
        return lines;
    }

    private ComposerStatus StatusOf(int unitCount, Readiness content)
    {
        if (unitCount == 0)
            return ComposerStatus.Empty;
        if (!ModelsReady && Output == ComposerOutput.Translate)
            return ComposerStatus.Working;
        if (content.IsReady)
            return ComposerStatus.Ready;
        return content.Reason == BlockReason.Processing ? ComposerStatus.Working : ComposerStatus.Failed;
    }
}
