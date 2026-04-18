using TranslationBenchmark;

namespace TranslationBenchmark.Tests;

public class LatencyStatsTests
{
    [Fact]
    public void Percentile_EmptyInput_ReturnsZero()
    {
        Assert.Equal(0, LatencyStats.Percentile(Array.Empty<long>(), 0.95));
    }

    [Fact]
    public void Percentile_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(42, LatencyStats.Percentile([42L], 0.95));
    }

    [Fact]
    public void Percentile_Interpolates_BetweenNeighbours()
    {
        // Sorted: [10, 20, 30, 40, 50]; P50 = 30; P95 = 48; P99 = 49.6
        long[] data = [50, 10, 40, 20, 30];
        Assert.Equal(30, LatencyStats.Percentile(data, 0.50));
        Assert.Equal(48, LatencyStats.Percentile(data, 0.95));
        Assert.Equal(49.6, LatencyStats.Percentile(data, 0.99), 5);
    }

    [Fact]
    public void Percentile_ClampsExtremes()
    {
        long[] data = [1, 2, 3];
        Assert.Equal(1, LatencyStats.Percentile(data, -0.1));
        Assert.Equal(3, LatencyStats.Percentile(data, 1.1));
    }

    [Fact]
    public void SimulatedFallbackRate_CountsOnlyOvershoot()
    {
        long?[] latencies = [100, 500, 800, 1500, null, 200];
        // 3 of 5 measured samples exceed 400ms (null is ignored) → 0.6
        Assert.Equal(0.6, LatencyStats.SimulatedFallbackRate(latencies, 400));
    }

    [Fact]
    public void SimulatedFallbackRate_NoSamples_ReturnsZero()
    {
        Assert.Equal(0, LatencyStats.SimulatedFallbackRate([null, null], 100));
        Assert.Equal(0, LatencyStats.SimulatedFallbackRate(Array.Empty<long?>(), 100));
    }

    [Fact]
    public void SimulatedFallbackRate_UsesStrictlyGreaterThan()
    {
        // A sample at exactly the budget should NOT count as fallback —
        // production `FallbackTranslationInvoker` races the budget.
        long?[] latencies = [400, 400, 401];
        Assert.Equal(1.0 / 3, LatencyStats.SimulatedFallbackRate(latencies, 400), 5);
    }
}
