using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Platform;

namespace LiveLingo.Desktop.Composer;

/// <summary>
/// State and commands of the composer window: the source text (typed, pasted or spoken), the result for the chosen
/// output, banners, listening and the write-back flow. It holds no UI types; the window binds to it and maps enums to text.
/// </summary>
public sealed partial class ComposerViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>A microphone press shorter than this is a tap (hands-free); a longer one is push-to-talk.</summary>
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(350);

    private readonly DraftSession _session;
    private readonly TranslationScheduler _scheduler;
    private readonly ComposerModelPlan _plan;
    private readonly IModelManager _models;
    private readonly IModelDownloadCoordinator _downloads;
    private readonly IPlatformServices _platform;
    private readonly IVoiceInput _voice;
    private readonly IReadOnlyList<string> _glossary;
    private readonly TargetWindowInfo? _target;
    private readonly SynchronizationContext? _notifyOn;
    private readonly TimeProvider _time;
    private CommitSnapshot? _committed;
    private bool _applyingDraftText;
    private bool _wantsVoice;
    private bool _stopAfterStart;
    private bool _tapStopsListening;
    private DateTimeOffset _micPressedAt;

    public ComposerViewModel(
        DraftSession session,
        TranslationScheduler scheduler,
        ComposerModelPlan plan,
        IModelManager models,
        IModelDownloadCoordinator downloads,
        IPlatformServices platform,
        IVoiceInput voice,
        IReadOnlyList<string> glossary,
        TargetWindowInfo? target,
        SynchronizationContext? notifyOn = null,
        TimeProvider? time = null)
    {
        _session = session;
        _scheduler = scheduler;
        _plan = plan;
        _models = models;
        _downloads = downloads;
        _platform = platform;
        _voice = voice;
        _glossary = glossary;
        _target = target;
        _notifyOn = notifyOn;
        _time = time ?? TimeProvider.System;
        _targetLanguage = scheduler.Target;

        _modelsReady = IsInstalled(_plan.For(_output));
        ApplyDraftOutput();
        _session.Changed += OnDraftChanged;
        _downloads.StateChanged += OnDownloadStateChanged;
        _voice.NoSignalChanged += OnNoSignalChanged;
        Refresh();
    }

    /// <summary>Raised after the result was written (or copied when there is no target); the window should close.</summary>
    public event Action? Completed;

    /// <summary>Raised when the user dismisses the composer with nothing pending.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private ComposerOutput _output = ComposerOutput.Clean;
    [ObservableProperty] private TargetLanguage _targetLanguage;
    [ObservableProperty] private IReadOnlyList<ResultLine> _lines = [];
    [ObservableProperty] private ComposerStatus _status = ComposerStatus.Empty;
    [ObservableProperty] private BannerState _banner = BannerState.None;
    [ObservableProperty] private bool _isWriteQueued;
    [ObservableProperty] private bool _canWrite;
    [ObservableProperty] private bool _canCopy;
    [ObservableProperty] private bool _isEditable = true;
    [ObservableProperty] private bool _modelsReady;
    /// <summary>The text that will be written once everything is ready; empty while sentences are still pending or failed.</summary>
    [ObservableProperty] private string _resultText = "";

    /// <summary>How many sentences kept their original wording because the cleanup failed the fidelity check.</summary>
    [ObservableProperty] private int _keptOriginalCount;

    [ObservableProperty] private bool _isListening;

    /// <summary>True while the speech models are loading after the microphone was pressed (takes a few seconds).</summary>
    [ObservableProperty] private bool _isStartingListening;

    public bool HasTarget => _target is not null;
    public string TargetName => _target?.ProcessName ?? "";

    partial void OnSourceTextChanged(string value)
    {
        if (_applyingDraftText || _committed is not null)
            return;

        CancelQueuedWrite();
        var route = TextUnits.RouteFor(value);
        var units = route == DraftRoute.Whole ? TextUnits.Whole(value) : TextUnits.Split(value);
        _session.Mutate(draft => draft.SyncSource(units, route));
    }

    partial void OnOutputChanged(ComposerOutput value)
    {
        CancelQueuedWrite();
        ModelsReady = IsInstalled(_plan.For(value));
        ApplyDraftOutput();
        if (Banner.Kind is BannerKind.ModelMissing or BannerKind.DownloadFailed)
            Banner = BannerState.None;
        Refresh();
    }

    partial void OnTargetLanguageChanged(TargetLanguage value)
    {
        CancelQueuedWrite();
        _scheduler.SetTarget(value);
    }

    partial void OnIsListeningChanged(bool value)
    {
        UpdateEditable();
        Refresh();
    }

    partial void OnIsStartingListeningChanged(bool value)
    {
        UpdateEditable();
        Refresh();
    }

    [RelayCommand]
    private void SwapLanguage() =>
        TargetLanguage = TargetLanguage == TargetLanguage.English ? TargetLanguage.Chinese : TargetLanguage.English;

    // ---- listening -----------------------------------------------------------

    /// <summary>The microphone was pressed: starts listening, or stops it when it was already on.</summary>
    public async Task PressMicAsync()
    {
        _micPressedAt = _time.GetUtcNow();
        _stopAfterStart = false;
        if (IsListening)
        {
            _tapStopsListening = true;
            await StopListeningAsync().ConfigureAwait(true);
            return;
        }

        _tapStopsListening = false;
        await StartListeningAsync().ConfigureAwait(true);
    }

    /// <summary>The microphone was released: a long press that started listening is push-to-talk and ends it.</summary>
    public async Task ReleaseMicAsync()
    {
        if (_tapStopsListening)
        {
            _tapStopsListening = false;
            return;
        }

        if (_time.GetUtcNow() - _micPressedAt < HoldThreshold)
            return;

        if (IsStartingListening)
            _stopAfterStart = true;
        else
            await StopListeningAsync().ConfigureAwait(true);
    }

    public async Task StartListeningAsync()
    {
        if (IsListening || IsStartingListening || _committed is not null)
            return;

        if (_platform.MicrophonePermission.Status == MicrophoneAccess.Denied)
        {
            Banner = new BannerState(BannerKind.MicrophoneDenied);
            return;
        }

        _wantsVoice = true;
        if (!IsInstalled(_plan.Voice))
        {
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
            return;
        }

        CancelQueuedWrite();
        IsStartingListening = true;
        try
        {
            await _voice.StartAsync(_session, _glossary, CancellationToken.None).ConfigureAwait(true);
            IsListening = true;
            if (Banner.Kind is BannerKind.MicrophoneDenied or BannerKind.NoMicrophone)
                Banner = BannerState.None;
        }
        catch (ModelNotReadyException)
        {
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
        }
        catch (InvalidOperationException ex)
        {
            Banner = new BannerState(BannerKind.NoMicrophone, ex.Message);
        }
        finally
        {
            IsStartingListening = false;
        }

        if (_stopAfterStart)
        {
            _stopAfterStart = false;
            await StopListeningAsync().ConfigureAwait(true);
        }
    }

    public async Task StopListeningAsync()
    {
        if (!IsListening)
            return;

        IsListening = false;
        if (Banner.Kind is BannerKind.NoSound)
            Banner = BannerState.None;
        await _voice.StopAsync().ConfigureAwait(true);
        ShowDraftSource();
        Refresh();
    }

    [RelayCommand]
    private Task ToggleListeningAsync() => IsListening ? StopListeningAsync() : StartListeningAsync();

    // ---- writing -------------------------------------------------------------

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (IsWriteQueued || _committed is not null)
            return;

        await StopListeningAsync().ConfigureAwait(true);
        if (Output != ComposerOutput.Original && !ModelsReady)
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
    private async Task CancelAsync()
    {
        if (IsListening)
            await StopListeningAsync().ConfigureAwait(true);
        else if (IsWriteQueued)
            CancelQueuedWrite();
        else
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Retry(int index)
    {
        var kind = Output == ComposerOutput.Clean ? UnitKind.Clean : UnitKind.Translation;
        _session.Mutate(draft => draft.Retry(index, kind));
    }

    [RelayCommand]
    private void OpenMicrophoneSettings() => _platform.MicrophonePermission.OpenSettings();

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        var wanted = _plan.For(Output).Concat(_wantsVoice ? _plan.Voice : []).DistinctBy(model => model.Id);
        foreach (var model in wanted.Where(model => !_models.HasAllExpectedLocalAssets(model)))
        {
            await _downloads.StartAsync(model).ConfigureAwait(true);
            if (!_models.HasAllExpectedLocalAssets(model))
            {
                var error = _downloads.GetState(model.Id).ErrorMessage ?? "";
                Banner = new BannerState(BannerKind.DownloadFailed, error);
                return;
            }
        }

        ModelsReady = IsInstalled(_plan.For(Output));
        Banner = BannerState.None;
        ApplyDraftOutput();
        RetryFailed();
        if (_wantsVoice && IsInstalled(_plan.Voice))
            await StartListeningAsync().ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        _session.Changed -= OnDraftChanged;
        _downloads.StateChanged -= OnDownloadStateChanged;
        _voice.NoSignalChanged -= OnNoSignalChanged;
        IsListening = false;
        await _voice.DisposeAsync().ConfigureAwait(false);
    }

    // ---- internals -----------------------------------------------------------

    private bool IsInstalled(IEnumerable<ModelDescriptor> models) => models.All(_models.HasAllExpectedLocalAssets);

    private void ApplyDraftOutput()
    {
        var output = !ModelsReady
            ? OutputKind.Raw
            : Output switch
            {
                ComposerOutput.Clean => OutputKind.Clean,
                ComposerOutput.Translate => OutputKind.Translation,
                _ => OutputKind.Raw
            };
        _session.Mutate(draft => draft.Output = output);
    }

    private void RetryFailed() =>
        _session.Mutate(draft =>
        {
            for (var i = 0; i < draft.Units.Count; i++)
            {
                if (draft.Units[i].Clean.Error.Length > 0)
                    draft.Retry(i, UnitKind.Clean);
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

    private void UpdateEditable() => IsEditable = _committed is null && !IsListening && !IsStartingListening;

    private async Task CompleteAsync(CommitSnapshot snapshot)
    {
        _committed = snapshot;
        UpdateEditable();
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
        if (IsListening || IsStartingListening)
            ShowDraftSource();

        Refresh();
        if (IsWriteQueued && Status == ComposerStatus.Ready)
            _ = WriteQueuedAsync();
        else if (IsWriteQueued && Status == ComposerStatus.Failed)
            CancelQueuedWrite();
    }

    /// <summary>While speaking, the source pane shows what the draft recognized; it is not the user's typing.</summary>
    private void ShowDraftSource()
    {
        var text = _session.Read(draft => draft.SourceText());
        if (text == SourceText)
            return;

        _applyingDraftText = true;
        try
        {
            SourceText = text;
        }
        finally
        {
            _applyingDraftText = false;
        }
    }

    private async Task WriteQueuedAsync()
    {
        IsWriteQueued = false;
        await WriteAsync().ConfigureAwait(true);
    }

    private void OnNoSignalChanged(bool silent)
    {
        void Apply()
        {
            if (silent && IsListening && Banner.Kind is BannerKind.None)
                Banner = new BannerState(BannerKind.NoSound);
            else if (!silent && Banner.Kind is BannerKind.NoSound)
                Banner = BannerState.None;
        }

        if (_notifyOn is null)
            Apply();
        else
            _notifyOn.Post(_ => Apply(), null);
    }

    private void OnDownloadStateChanged(ModelDownloadState state)
    {
        var wanted = _plan.For(Output).Concat(_wantsVoice ? _plan.Voice : []).DistinctBy(model => model.Id).ToList();
        if (wanted.All(model => model.Id != state.ModelId))
            return;

        void Apply()
        {
            if (state.Status == ModelDownloadStatus.Downloading)
                Banner = new BannerState(BannerKind.Downloading, "", OverallProgress(wanted));
        }

        if (_notifyOn is null)
            Apply();
        else
            _notifyOn.Post(_ => Apply(), null);
    }

    private double OverallProgress(IReadOnlyList<ModelDescriptor> wanted)
    {
        var total = wanted.Sum(model => (double)model.SizeBytes);
        var done = wanted.Sum(model => model.SizeBytes * _downloads.GetState(model.Id).Percentage / 100);
        return total > 0 ? done / total * 100 : 0;
    }

    private void Refresh()
    {
        var snapshot = _session.Read(draft => (
            Lines: BuildLines(draft),
            Content: draft.ContentReadiness(),
            Count: draft.Units.Count,
            HasPending: draft.Pending.Length > 0,
            Text: draft.ContentReadiness().IsReady ? draft.Text() : "",
            Kept: Output == ComposerOutput.Clean ? draft.Units.Count(unit => unit.Clean.Warning.Length > 0) : 0));

        Lines = snapshot.Lines;
        Status = StatusOf(snapshot.Count + (snapshot.HasPending ? 1 : 0), snapshot.Content);
        ResultText = Status == ComposerStatus.Ready ? snapshot.Text : "";
        KeptOriginalCount = snapshot.Kept;
        CanCopy = _committed is not null || Status == ComposerStatus.Ready;
        CanWrite = CanCopy && _committed is null;

        var needsModels = !ModelsReady && Output != ComposerOutput.Original;
        if (needsModels && Banner.Kind is BannerKind.None)
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
    }

    private string MissingSizeLabel()
    {
        var wanted = _plan.For(Output).Concat(_wantsVoice ? _plan.Voice : []).DistinctBy(model => model.Id);
        var bytes = wanted.Where(model => !_models.HasAllExpectedLocalAssets(model)).Sum(model => model.SizeBytes);
        return $"{bytes / 1_000_000_000d:0.0} GB";
    }

    private IReadOnlyList<ResultLine> BuildLines(Draft draft)
    {
        var lines = new List<ResultLine>(draft.Units.Count);
        for (var i = 0; i < draft.Units.Count; i++)
        {
            var unit = draft.Units[i];
            if (Output == ComposerOutput.Original)
            {
                lines.Add(new ResultLine(i, unit.Raw, LineState.Ready, ""));
                continue;
            }

            var result = Output == ComposerOutput.Clean ? unit.Clean : unit.Translation;
            var state = ModelsReady
                ? draft.StateOf(unit, Output == ComposerOutput.Clean ? OutputKind.Clean : OutputKind.Translation) switch
                {
                    UnitState.Ready => LineState.Ready,
                    UnitState.Error => LineState.Failed,
                    UnitState.Empty => LineState.Empty,
                    _ => LineState.Pending
                }
                : LineState.Pending;
            lines.Add(new ResultLine(i, result.Text ?? "", state, result.Error));
        }
        return lines;
    }

    private ComposerStatus StatusOf(int itemCount, Readiness content)
    {
        if (IsStartingListening)
            return ComposerStatus.Starting;
        if (IsListening)
            return ComposerStatus.Listening;
        if (itemCount == 0)
            return ComposerStatus.Empty;
        if (!ModelsReady && Output != ComposerOutput.Original)
            return ComposerStatus.Working;
        if (content.IsReady)
            return ComposerStatus.Ready;
        return content.Reason == BlockReason.Processing ? ComposerStatus.Working : ComposerStatus.Failed;
    }
}
