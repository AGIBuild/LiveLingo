namespace TranslationBenchmark.Models;

public sealed record CaseResult(
    string CaseId,
    string Domain,
    string Source,
    string Reference,
    string Translation,
    double BleuScore,
    double? JudgeScore,
    long ElapsedMs,
    long? FirstTokenMs = null,
    string? Error = null
)
{
    public bool IsSuccess => Error is null;
}

public sealed record ModelBenchmarkResult(
    string ModelName,
    string PromptVariant,
    string LanguagePair,
    IReadOnlyList<CaseResult> Cases
)
{
    public double AvgBleu =>
        Cases.Where(c => c.IsSuccess).Select(c => c.BleuScore).DefaultIfEmpty(0).Average();

    public double? AvgJudge =>
        Cases.Where(c => c.IsSuccess && c.JudgeScore.HasValue).Select(c => c.JudgeScore!.Value) is var s && s.Any()
            ? s.Average()
            : null;

    public double AvgElapsedMs =>
        Cases.Where(c => c.IsSuccess).Select(c => (double)c.ElapsedMs).DefaultIfEmpty(0).Average();

    public int SuccessCount => Cases.Count(c => c.IsSuccess);
    public int TotalCount => Cases.Count;

    private IReadOnlyList<long> SuccessfulElapsedMs =>
        Cases.Where(c => c.IsSuccess).Select(c => c.ElapsedMs).ToArray();

    private IReadOnlyList<long> SuccessfulFirstTokenMs =>
        Cases.Where(c => c.IsSuccess && c.FirstTokenMs.HasValue).Select(c => c.FirstTokenMs!.Value).ToArray();

    public bool HasFirstTokenSamples => SuccessfulFirstTokenMs.Count > 0;

    public double P50ElapsedMs => LatencyStats.Percentile(SuccessfulElapsedMs, 0.50);
    public double P95ElapsedMs => LatencyStats.Percentile(SuccessfulElapsedMs, 0.95);
    public double P99ElapsedMs => LatencyStats.Percentile(SuccessfulElapsedMs, 0.99);

    public double P50FirstTokenMs => LatencyStats.Percentile(SuccessfulFirstTokenMs, 0.50);
    public double P95FirstTokenMs => LatencyStats.Percentile(SuccessfulFirstTokenMs, 0.95);
    public double P99FirstTokenMs => LatencyStats.Percentile(SuccessfulFirstTokenMs, 0.99);

    /// <summary>
    /// Fraction of successful streaming calls whose first-token latency would
    /// trip the given budget — i.e. what <c>UsedFallbackRate</c> would look like
    /// in production if this model were the primary candidate. Returns 0 if no
    /// first-token samples are available (e.g. non-streaming run).
    /// </summary>
    public double SimulatedFallbackRate(long firstTokenBudgetMs) =>
        LatencyStats.SimulatedFallbackRate(Cases.Select(c => c.FirstTokenMs), firstTokenBudgetMs);
}
