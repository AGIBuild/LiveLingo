using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;

namespace LiveLingo.Desktop.Composer;

/// <summary>Importing a recording: it becomes a read-only timeline of segments that go through the same cleanup and translation as speech.</summary>
public sealed partial class ComposerViewModel
{
    private CancellationTokenSource? _transcribing;
    private string? _pendingImport;
    private IReadOnlyList<TimedSegment> _segments = [];

    [ObservableProperty] private bool _isTranscribing;

    /// <summary>0–100 while a recording is being transcribed.</summary>
    [ObservableProperty] private double _transcribeProgress;

    /// <summary>True once a recording was imported; the source pane is then a read-only transcript.</summary>
    [ObservableProperty] private bool _hasTimeline;

    /// <summary>The source and the result sit side by side: a whole document or a transcript has room to breathe.</summary>
    [ObservableProperty] private bool _isWide;

    /// <summary>A recording replaces the draft, so it can only be imported into an empty, idle composer.</summary>
    public bool CanImport =>
        _committed is null && !IsListening && !IsStartingListening && !IsTranscribing && !HasTimeline
        && string.IsNullOrWhiteSpace(SourceText);

    public bool CanExport => HasTimeline && Status == ComposerStatus.Ready;

    partial void OnIsTranscribingChanged(bool value)
    {
        UpdateEditable();
        Refresh();
    }

    partial void OnHasTimelineChanged(bool value)
    {
        UpdateEditable();
        Refresh();
    }

    /// <summary>Transcribes a WAV recording on this machine and shows it as a timeline.</summary>
    public async Task ImportAsync(string path)
    {
        if (!CanImport)
            return;

        CancelQueuedWrite();
        _pendingImport = path;
        if (!IsInstalled(_plan.Transcription))
        {
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
            return;
        }

        await RunImportAsync(path).ConfigureAwait(true);
    }

    private async Task RunImportAsync(string path)
    {
        _pendingImport = null;
        Banner = BannerState.None;
        using var cancellation = _transcribing = new CancellationTokenSource();
        TranscribeProgress = 0;
        IsTranscribing = true;
        try
        {
            var progress = new Progress<double>(fraction => TranscribeProgress = fraction * 100);
            var segments = await _transcriber.TranscribeAsync(path, _glossary, progress, cancellation.Token).ConfigureAwait(true);
            if (segments.Count == 0)
                Banner = new BannerState(BannerKind.NoSpeechFound);
            else
                ShowTimeline(segments);
        }
        catch (OperationCanceledException)
        {
            // The user pressed Esc: the composer stays empty.
        }
        catch (ModelNotReadyException)
        {
            _pendingImport = path;
            Banner = new BannerState(BannerKind.ModelMissing, MissingSizeLabel());
        }
        catch (Exception ex) when (ex is UnsupportedAudioException or IOException or UnauthorizedAccessException)
        {
            Banner = new BannerState(BannerKind.FileFailed, ex.Message);
        }
        finally
        {
            _transcribing = null;
            IsTranscribing = false;
        }
    }

    private void ShowTimeline(IReadOnlyList<TimedSegment> segments)
    {
        _segments = segments;
        var units = segments
            .Select((segment, i) => new SourceUnit(segment.Text, Trail: i < segments.Count - 1 ? "\n" : ""))
            .ToList();
        _session.Mutate(draft => draft.SyncSource(units, DraftRoute.Units));
        HasTimeline = true;
        ShowDraftSource();
        Refresh();
    }

    private string TimeLabel(int index)
    {
        if (index >= _segments.Count)
            return "";

        var start = _segments[index].Start;
        return start.TotalHours >= 1
            ? start.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : start.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Subtitles for the chosen output (cleaned, translated or original), one cue per recognized stretch.</summary>
    public string BuildSrt() =>
        SrtWriter.Write(_segments.Select((segment, i) => segment with { Text = i < Lines.Count ? Lines[i].Text : "" }));

    /// <summary>Saves <see cref="BuildSrt"/>; a file that cannot be written is reported in a banner.</summary>
    public async Task ExportSrtAsync(string path)
    {
        if (!CanExport)
            return;

        try
        {
            await File.WriteAllTextAsync(path, BuildSrt(), new System.Text.UTF8Encoding(false)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Banner = new BannerState(BannerKind.FileFailed, ex.Message);
        }
    }
}
