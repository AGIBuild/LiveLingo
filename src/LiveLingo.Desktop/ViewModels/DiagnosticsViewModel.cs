using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.ViewModels;

/// <summary>
/// Presentation model for the Settings → Diagnostics tab. Subscribes to
/// <see cref="ITranslationTelemetry.RouteCompleted"/> and keeps a bounded, most-recent-first
/// <see cref="ObservableCollection{T}"/> of <see cref="TranslationTraceRow"/> rows bound
/// by the view.
///
/// Architecture contract: this VM never touches Avalonia directly. UI marshalling is
/// done through a <see cref="SynchronizationContext"/> captured at construction, which
/// keeps the VM testable with NSubstitute and unit-testable without a dispatcher.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    /// <summary>Row cap shown in the panel. Independent from the telemetry ring buffer capacity.</summary>
    public const int PanelCapacity = 80;

    private readonly ITranslationTelemetry _telemetry;
    private readonly ILocalizationService? _loc;
    private readonly SynchronizationContext? _uiContext;
    private readonly Action<TranslationRouteTrace> _onRouteCompleted;
    private bool _disposed;

    [ObservableProperty] private TranslationTraceRow? _selectedTrace;

    public ObservableCollection<TranslationTraceRow> Traces { get; } = [];

    public DiagnosticsViewModel(
        ITranslationTelemetry telemetry,
        ILocalizationService? localizationService = null,
        SynchronizationContext? uiContext = null)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _loc = localizationService;
        _uiContext = uiContext ?? SynchronizationContext.Current;

        // RecentTraces is oldest-first; the panel shows newest-first with a bounded size.
        foreach (var trace in _telemetry.RecentTraces.Reverse().Take(PanelCapacity))
            Traces.Add(new TranslationTraceRow(trace));

        _onRouteCompleted = HandleRouteCompleted;
        _telemetry.RouteCompleted += _onRouteCompleted;
    }

    public int TotalCount => Traces.Count;
    public int UsedFallbackCount => Traces.Count(r => r.UsedFallback);
    public int FailedCount => Traces.Count(r =>
        r.Trace.Outcome is TranslationRouteTraceOutcome.AllCandidatesFailed
                        or TranslationRouteTraceOutcome.FailedAfterFirstToken);
    public int SucceededCount => Traces.Count(r => r.Trace.Outcome == TranslationRouteTraceOutcome.Succeeded);

    public double UsedFallbackRate => TotalCount == 0 ? 0 : (double)UsedFallbackCount / TotalCount;
    public double FailureRate => TotalCount == 0 ? 0 : (double)FailedCount / TotalCount;
    public double SuccessRate => TotalCount == 0 ? 0 : (double)SucceededCount / TotalCount;

    public string SummaryCaption =>
        TotalCount == 0
            ? L("settings.diagnostics.empty", "No translations recorded yet.")
            : L("settings.diagnostics.summaryFmt",
                "{0} traces · {1} used fallback ({2:P0}) · {3} failed ({4:P0})",
                TotalCount, UsedFallbackCount, UsedFallbackRate, FailedCount, FailureRate);

    public bool HasTraces => Traces.Count > 0;

    public string PanelHint => L(
        "settings.diagnostics.hint",
        "Most recent first. Showing up to {0} traces; the telemetry buffer stores up to {1}.",
        PanelCapacity,
        InProcessTranslationTelemetry.DefaultCapacity);

    public string ClearLabel => L("settings.diagnostics.clear", "Clear");
    public string AttemptsHeader => L("settings.diagnostics.attemptsHeader", "Attempts");
    public string NoSelectionHint => L("settings.diagnostics.noSelection", "Select a trace to inspect its attempts.");
    public string StartedAtLabel => L("settings.diagnostics.startedAt", "Started:");
    public string KindLabel => L("settings.diagnostics.kind", "Kind:");
    public string OutcomeLabel => L("settings.diagnostics.outcome", "Outcome:");
    public string DurationLabel => L("settings.diagnostics.duration", "Duration:");
    public string WinningCandidateLabel => L("settings.diagnostics.winner", "Winner:");

    [RelayCommand]
    private void Clear()
    {
        _telemetry.Clear();
        Traces.Clear();
        SelectedTrace = null;
        NotifySummaryChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _telemetry.RouteCompleted -= _onRouteCompleted;
    }

    private void HandleRouteCompleted(TranslationRouteTrace trace)
    {
        if (_disposed) return;
        var row = new TranslationTraceRow(trace);

        if (_uiContext is not null)
            _uiContext.Post(_ => AppendAndNotify(row), null);
        else
            AppendAndNotify(row);
    }

    private void AppendAndNotify(TranslationTraceRow row)
    {
        if (_disposed) return;
        AppendRow(row);
        NotifySummaryChanged();
    }

    private void AppendRow(TranslationTraceRow row)
    {
        Traces.Insert(0, row);
        while (Traces.Count > PanelCapacity)
            Traces.RemoveAt(Traces.Count - 1);
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UsedFallbackCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(SucceededCount));
        OnPropertyChanged(nameof(UsedFallbackRate));
        OnPropertyChanged(nameof(FailureRate));
        OnPropertyChanged(nameof(SuccessRate));
        OnPropertyChanged(nameof(SummaryCaption));
        OnPropertyChanged(nameof(HasTraces));
    }

    private string L(string key, string fallback)
        => _loc is not null && _loc.TryT(key, out var value) ? value : fallback;
    private string L(string key, string fallback, params object[] args)
    {
        if (_loc is not null && _loc.TryT(key, out var template))
        {
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
        return string.Format(fallback, args);
    }
}
