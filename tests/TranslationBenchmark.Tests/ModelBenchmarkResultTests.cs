using TranslationBenchmark;
using TranslationBenchmark.Models;

namespace TranslationBenchmark.Tests;

public class ModelBenchmarkResultTests
{
    [Fact]
    public void Percentiles_ComputedFromSuccessfulCases()
    {
        var cases = new List<CaseResult>
        {
            Ok("c1", 10, firstTokenMs: 2),
            Ok("c2", 20, firstTokenMs: 4),
            Ok("c3", 30, firstTokenMs: 6),
            Ok("c4", 40, firstTokenMs: 8),
            Ok("c5", 50, firstTokenMs: 10),
            Failed("c6"),
        };

        var result = new ModelBenchmarkResult("m", "v", "zh→en", cases);

        Assert.Equal(30, result.P50ElapsedMs);
        Assert.Equal(48, result.P95ElapsedMs);
        Assert.Equal(6, result.P50FirstTokenMs);
        Assert.Equal(9.6, result.P95FirstTokenMs, 5);
        Assert.True(result.HasFirstTokenSamples);
    }

    [Fact]
    public void HasFirstTokenSamples_FalseForNonStreamingRun()
    {
        var cases = new List<CaseResult> { Ok("c1", 100, firstTokenMs: null) };
        var result = new ModelBenchmarkResult("m", "default", "zh→en", cases);
        Assert.False(result.HasFirstTokenSamples);
    }

    [Fact]
    public void SimulatedFallbackRate_Aligns_WithLatencyStats()
    {
        var cases = new List<CaseResult>
        {
            Ok("c1", 100, firstTokenMs: 200),
            Ok("c2", 200, firstTokenMs: 450),
            Ok("c3", 300, firstTokenMs: 800),
            Ok("c4", 400, firstTokenMs: 1200),
            Failed("c5"),
        };
        var result = new ModelBenchmarkResult("m", "v", "zh→en", cases);
        // budget 400 → 3 of 4 over
        Assert.Equal(0.75, result.SimulatedFallbackRate(400));
        // budget 1000 → 1 of 4 over
        Assert.Equal(0.25, result.SimulatedFallbackRate(1000));
    }

    private static CaseResult Ok(string id, long ms, long? firstTokenMs) =>
        new(id, "demo", "src", "ref", "trans", BleuScore: 0.5, JudgeScore: null,
            ElapsedMs: ms, FirstTokenMs: firstTokenMs);

    private static CaseResult Failed(string id) =>
        new(id, "demo", "src", "ref", "", BleuScore: 0, JudgeScore: null,
            ElapsedMs: 0, FirstTokenMs: null, Error: "timeout");
}
