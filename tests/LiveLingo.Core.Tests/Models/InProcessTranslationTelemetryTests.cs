using LiveLingo.Core.Models;

namespace LiveLingo.Core.Tests.Models;

public sealed class InProcessTranslationTelemetryTests
{
    private static readonly TranslationRouteCandidate Candidate = new(
        new ModelProfile(
            "m", "M", ModelTaskType.Translation,
            ModelProviderKind.LlamaServer, ModelRuntimeKind.LlamaServer,
            ModelExecutionKind.ChatCompletions, [],
            new ModelDescriptor("m", "M", "", 0, ModelType.Translation),
            SupportsAllLanguages: true),
        TranslationRouteTier.Local,
        TimeSpan.FromSeconds(5));

    private static TranslationRouteTrace Trace(int seed) =>
        new(DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(seed),
            TranslationRouteInvocationKind.NonStreaming,
            TranslationRouteTraceOutcome.Succeeded,
            Candidate,
            [
                new TranslationRouteAttempt(
                    Candidate,
                    TranslationRouteAttemptOutcome.Succeeded,
                    TimeSpan.FromMilliseconds(seed),
                    FirstTokenLatency: null,
                    FailureReason: null)
            ]);

    [Fact]
    public void Constructor_Rejects_InvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InProcessTranslationTelemetry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InProcessTranslationTelemetry(-1));
    }

    [Fact]
    public void Record_AppendsTraces_InOrder()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: 10);
        telemetry.Record(Trace(1));
        telemetry.Record(Trace(2));
        telemetry.Record(Trace(3));

        var traces = telemetry.RecentTraces;
        Assert.Equal(3, traces.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1), traces[0].TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(3), traces[^1].TotalDuration);
    }

    [Fact]
    public void Record_EvictsOldestWhenCapacityExceeded()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: 2);
        telemetry.Record(Trace(1));
        telemetry.Record(Trace(2));
        telemetry.Record(Trace(3));

        var traces = telemetry.RecentTraces;
        Assert.Equal(2, traces.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2), traces[0].TotalDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(3), traces[1].TotalDuration);
    }

    [Fact]
    public void Record_RaisesRouteCompletedEvent_ForEachTrace()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: 5);
        var received = new List<TranslationRouteTrace>();
        telemetry.RouteCompleted += received.Add;

        var t1 = Trace(1);
        var t2 = Trace(2);
        telemetry.Record(t1);
        telemetry.Record(t2);

        Assert.Equal([t1, t2], received);
    }

    [Fact]
    public void Record_Throws_OnNullTrace()
    {
        var telemetry = new InProcessTranslationTelemetry();
        Assert.Throws<ArgumentNullException>(() => telemetry.Record(null!));
    }

    [Fact]
    public void RecentTraces_ReturnsIndependentSnapshot()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: 5);
        telemetry.Record(Trace(1));

        var snapshot = telemetry.RecentTraces;
        telemetry.Record(Trace(2));

        // Snapshot taken before the second Record() must not reflect the later insert.
        Assert.Single(snapshot);
        Assert.Equal(2, telemetry.RecentTraces.Count);
    }

    [Fact]
    public void Clear_EmptiesBuffer_WithoutRaisingEvents()
    {
        var telemetry = new InProcessTranslationTelemetry(capacity: 5);
        telemetry.Record(Trace(1));
        telemetry.Record(Trace(2));

        var raisedAfterClear = 0;
        telemetry.RouteCompleted += _ => raisedAfterClear++;

        telemetry.Clear();

        Assert.Empty(telemetry.RecentTraces);
        Assert.Equal(0, raisedAfterClear);

        telemetry.Record(Trace(3));
        Assert.Single(telemetry.RecentTraces);
    }

    [Fact]
    public async Task Record_IsThreadSafe_UnderConcurrentWriters()
    {
        const int writers = 8;
        const int perWriter = 250;
        var telemetry = new InProcessTranslationTelemetry(capacity: writers * perWriter);

        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
                telemetry.Record(Trace(w * perWriter + i));
        })));

        Assert.Equal(writers * perWriter, telemetry.RecentTraces.Count);
    }
}
