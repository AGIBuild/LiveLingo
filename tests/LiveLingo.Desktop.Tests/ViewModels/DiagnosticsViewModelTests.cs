using System.Collections.Specialized;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.ViewModels;

namespace LiveLingo.Desktop.Tests.ViewModels;

public sealed class DiagnosticsViewModelTests
{
    private static readonly TranslationRouteCandidate Candidate = new(
        new ModelProfile(
            "m", "Model", ModelTaskType.Translation,
            ModelProviderKind.LlamaServer, ModelRuntimeKind.LlamaServer,
            ModelExecutionKind.ChatCompletions, [],
            new ModelDescriptor("m", "Model", "", 0, ModelType.Translation),
            SupportsAllLanguages: true),
        TranslationRouteTier.Local,
        TimeSpan.FromSeconds(5));

    private static TranslationRouteTrace MakeTrace(
        TranslationRouteTraceOutcome outcome = TranslationRouteTraceOutcome.Succeeded,
        int attempts = 1,
        int seed = 1)
    {
        var list = new List<TranslationRouteAttempt>();
        for (var i = 0; i < attempts; i++)
        {
            list.Add(new TranslationRouteAttempt(
                Candidate,
                i < attempts - 1
                    ? TranslationRouteAttemptOutcome.FailedWithException
                    : outcome switch
                    {
                        TranslationRouteTraceOutcome.Succeeded => TranslationRouteAttemptOutcome.Succeeded,
                        TranslationRouteTraceOutcome.AllCandidatesFailed => TranslationRouteAttemptOutcome.FailedWithException,
                        TranslationRouteTraceOutcome.FailedAfterFirstToken => TranslationRouteAttemptOutcome.FailedAfterFirstToken,
                        TranslationRouteTraceOutcome.Cancelled => TranslationRouteAttemptOutcome.Cancelled,
                        _ => TranslationRouteAttemptOutcome.FailedWithException
                    },
                TimeSpan.FromMilliseconds(seed),
                FirstTokenLatency: null,
                FailureReason: i < attempts - 1 ? "boom" : null));
        }
        return new TranslationRouteTrace(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(seed),
            TranslationRouteInvocationKind.NonStreaming,
            outcome,
            outcome == TranslationRouteTraceOutcome.Succeeded ? Candidate : null,
            list);
    }

    [Fact]
    public void Constructor_PopulatesFromExistingTelemetry_InMostRecentOrder()
    {
        var telemetry = new InProcessTranslationTelemetry();
        telemetry.Record(MakeTrace(seed: 1));
        telemetry.Record(MakeTrace(seed: 2));

        var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        Assert.Equal(2, vm.Traces.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2), vm.Traces[0].Trace.TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(1), vm.Traces[1].Trace.TotalDuration);
    }

    [Fact]
    public void RouteCompleted_InsertsAtTop_WithoutUIContext()
    {
        var telemetry = new InProcessTranslationTelemetry();
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        telemetry.Record(MakeTrace(seed: 1));
        telemetry.Record(MakeTrace(seed: 2));

        Assert.Equal(2, vm.Traces.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2), vm.Traces[0].Trace.TotalDuration);
    }

    [Fact]
    public void RouteCompleted_EvictsOldestRowsBeyondPanelCapacity()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: DiagnosticsViewModel.PanelCapacity * 2);
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        for (var i = 0; i < DiagnosticsViewModel.PanelCapacity + 5; i++)
            telemetry.Record(MakeTrace(seed: i + 1));

        Assert.Equal(DiagnosticsViewModel.PanelCapacity, vm.Traces.Count);
        // Most recent should be first; oldest kept row is seed = 6 (first 5 evicted).
        Assert.Equal(
            TimeSpan.FromMilliseconds(DiagnosticsViewModel.PanelCapacity + 5),
            vm.Traces[0].Trace.TotalDuration);
        Assert.Equal(
            TimeSpan.FromMilliseconds(6),
            vm.Traces[^1].Trace.TotalDuration);
    }

    [Fact]
    public void Summary_ReflectsOutcomeCounts()
    {
        var telemetry = new InProcessTranslationTelemetry();
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        telemetry.Record(MakeTrace(seed: 1));
        telemetry.Record(MakeTrace(TranslationRouteTraceOutcome.Succeeded, attempts: 2, seed: 2));
        telemetry.Record(MakeTrace(TranslationRouteTraceOutcome.AllCandidatesFailed, seed: 3));
        telemetry.Record(MakeTrace(TranslationRouteTraceOutcome.FailedAfterFirstToken, seed: 4));

        Assert.Equal(4, vm.TotalCount);
        Assert.Equal(2, vm.SucceededCount);
        Assert.Equal(1, vm.UsedFallbackCount);
        Assert.Equal(2, vm.FailedCount);
        Assert.Equal(0.5, vm.FailureRate);
        Assert.Equal(0.25, vm.UsedFallbackRate);
    }

    [Fact]
    public void ClearCommand_EmptiesTelemetryAndPanel_AndResetsSelection()
    {
        var telemetry = new InProcessTranslationTelemetry();
        telemetry.Record(MakeTrace(seed: 1));
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);
        vm.SelectedTrace = vm.Traces[0];

        vm.ClearCommand.Execute(null);

        Assert.Empty(vm.Traces);
        Assert.Empty(telemetry.RecentTraces);
        Assert.Null(vm.SelectedTrace);
        Assert.Equal(0, vm.TotalCount);
    }

    [Fact]
    public void Dispose_DetachesFromTelemetry_SoFurtherRecordsDontMutatePanel()
    {
        var telemetry = new InProcessTranslationTelemetry();
        var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        vm.Dispose();
        telemetry.Record(MakeTrace(seed: 1));

        Assert.Empty(vm.Traces);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var telemetry = new InProcessTranslationTelemetry();
        var vm = new DiagnosticsViewModel(telemetry, uiContext: null);
        vm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public void Traces_RaiseCollectionChanged_OnNewTrace()
    {
        var telemetry = new InProcessTranslationTelemetry();
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        NotifyCollectionChangedAction? action = null;
        vm.Traces.CollectionChanged += (_, e) => action = e.Action;

        telemetry.Record(MakeTrace(seed: 1));

        Assert.Equal(NotifyCollectionChangedAction.Add, action);
    }

    [Fact]
    public void Row_Exposes_FormattedMetadata()
    {
        var telemetry = new InProcessTranslationTelemetry();
        telemetry.Record(MakeTrace(TranslationRouteTraceOutcome.Succeeded, attempts: 2, seed: 120));
        using var vm = new DiagnosticsViewModel(telemetry, uiContext: null);

        var row = vm.Traces[0];
        Assert.True(row.UsedFallback);
        Assert.Contains("local", row.WinningCandidateLabel);
        Assert.Equal(2, row.Attempts.Count);
        Assert.Equal("once", row.KindLabel);
    }
}
