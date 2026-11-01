using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Desktop.Composer;

/// <summary>The draft as a whole: new draft, layout and target choices, the recording clock, slow-processing notice and banner buttons.</summary>
public sealed partial class ComposerViewModel
{
    /// <summary>How long the selected output may take before the user is offered the original words.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(8);

    private ITimer? _recordingClock;
    private ITimer? _slowTimer;

    /// <summary>The imported file shown in the source label.</summary>
    [ObservableProperty] private string _transcriptName = "";

    [ObservableProperty] private int _recordingSeconds;

    /// <summary>Process the source as one document, keeping its layout (the "whole" menu item).</summary>
    [ObservableProperty] private bool _forceWhole;

    /// <summary>Always show the source and the result side by side (the "wide" menu item).</summary>
    [ObservableProperty] private bool _forceWide;

    /// <summary>The user chose "no input box · copy only" for this draft.</summary>
    [ObservableProperty] private bool _copyOnly;

    /// <summary>The window the text was meant for no longer exists.</summary>
    [ObservableProperty] private bool _isTargetLost;

    /// <summary>The source is processed as one document, so the "whole" chip shows next to the label.</summary>
    [ObservableProperty] private bool _isWholeRoute;

    /// <summary>The sentences of the source pane while listening or showing a transcript.</summary>
    [ObservableProperty] private IReadOnlyList<SourceLine> _sourceLines = [];

    public bool CloudTranslate => _options.CloudTranslate;
    /// <summary>Cleanup runs on the user's endpoint; false when cleanup is off, because then nothing is sent.</summary>
    public bool CloudClean => _options.CloudClean && _options.Cleanup;

    /// <summary>The outputs the pill offers, in menu order.</summary>
    public IReadOnlyList<ComposerOutput> Outputs { get; }
    public CommitKey CommitKey => _options.CommitKey;
    public bool Animations => _options.Animations;
    public string Theme => _options.Theme;

    /// <summary>The user wants the settings (the section id is "voice" for the input device).</summary>
    public event Action<string>? SettingsRequested;

    /// <summary>The target chip should open its menu so another destination can be chosen.</summary>
    public event Action? RetargetRequested;

    partial void OnForceWholeChanged(bool value) => ResyncSource();

    partial void OnForceWideChanged(bool value) => Refresh();

    partial void OnCopyOnlyChanged(bool value)
    {
        CancelQueuedWrite();
        OnPropertyChanged(nameof(HasTarget));
        var hasTarget = HasTarget;
        _session.Mutate(draft => draft.Retarget(hasTarget));
    }

    partial void OnIsTargetLostChanged(bool value) => OnPropertyChanged(nameof(HasTarget));

    /// <summary>Stops everything and empties the composer.</summary>
    [RelayCommand]
    private async Task NewDraftAsync()
    {
        await StopListeningAsync().ConfigureAwait(true);
        _transcribing?.Cancel();
        CancelQueuedWrite();
        EditingIndex = -1;
        _committed = null;
        _segments = [];
        HasTimeline = false;
        _applyingDraftText = true;
        try
        {
            SourceText = "";
        }
        finally
        {
            _applyingDraftText = false;
        }

        var hasTarget = HasTarget;
        _session.Mutate(draft => draft.Reset(hasTarget));
        ApplyDraftOutput();
        Banner = BannerState.None;
        UpdateEditable();
        Refresh();
    }

    [RelayCommand]
    private async Task RunBannerActionAsync(BannerAction action)
    {
        switch (action)
        {
            case BannerAction.Dismiss:
                Banner = BannerState.None;
                break;
            case BannerAction.Download:
                await DownloadModelsCommand.ExecuteAsync(null).ConfigureAwait(true);
                break;
            case BannerAction.OpenMicrophoneSettings:
                _platform.MicrophonePermission.OpenSettings();
                break;
            case BannerAction.OpenInputSettings:
                Banner = BannerState.None;
                SettingsRequested?.Invoke("voice");
                break;
            case BannerAction.UseOriginal:
                Banner = BannerState.None;
                Output = ComposerOutput.Original;
                break;
            case BannerAction.KeepWaiting:
                Banner = BannerState.None;
                RestartSlowTimer();
                break;
            case BannerAction.Retarget:
                RetargetRequested?.Invoke();
                break;
            case BannerAction.Copy:
                await CopyCommand.ExecuteAsync(null).ConfigureAwait(true);
                break;
        }
    }

    private void UpdateRecordingClock(bool listening)
    {
        _recordingClock?.Dispose();
        _recordingClock = null;
        RecordingSeconds = 0;
        if (!listening)
            return;

        _recordingClock = _time.CreateTimer(
            _ => OnUiThread(() => RecordingSeconds++), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Starts or stops the slow-processing countdown as the status enters or leaves "working".</summary>
    private void UpdateSlowTimer()
    {
        if (Status == ComposerStatus.Working)
        {
            if (_slowTimer is null)
                RestartSlowTimer();
            return;
        }

        _slowTimer?.Dispose();
        _slowTimer = null;
        if (Banner.Kind == BannerKind.SlowProcessing)
            Banner = BannerState.None;
    }

    private void RestartSlowTimer()
    {
        _slowTimer?.Dispose();
        _slowTimer = _time.CreateTimer(_ => OnUiThread(ShowSlowNotice), null, SlowAfter, Timeout.InfiniteTimeSpan);
    }

    private void ShowSlowNotice()
    {
        if (Status == ComposerStatus.Working && Banner.Kind == BannerKind.None)
            Banner = new BannerState(BannerKind.SlowProcessing);
    }

    private void OnUiThread(Action action)
    {
        if (_notifyOn is null)
            action();
        else
            _notifyOn.Post(_ => action(), null);
    }

    private void DisposeTimers()
    {
        _recordingClock?.Dispose();
        _slowTimer?.Dispose();
        _transcribing?.Cancel();
    }

    private static IReadOnlyList<SourceLine> BuildSourceLines(Draft draft, IReadOnlyList<string> times)
    {
        var lines = new List<SourceLine>(draft.Units.Count + 1);
        for (var i = 0; i < draft.Units.Count; i++)
            lines.Add(new SourceLine(i < times.Count ? times[i] : "", draft.Units[i].Raw, false));
        if (draft.Pending.Length > 0)
            lines.Add(new SourceLine("", draft.Pending, true));
        return lines;
    }
}
