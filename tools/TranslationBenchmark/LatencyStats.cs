namespace TranslationBenchmark;

/// <summary>
/// Latency and distribution helpers for benchmark reporting. Intentionally
/// ignorant of the rest of the pipeline so they can be unit-tested in isolation.
/// </summary>
public static class LatencyStats
{
    /// <summary>
    /// Linear-interpolation percentile (same method as NumPy's default), clamped
    /// to sorted input. Returns 0 for an empty sample.
    /// </summary>
    /// <param name="p">Percentile in the range [0, 1]. 0.95 == P95.</param>
    public static double Percentile(IReadOnlyList<long> values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return 0;
        if (p <= 0) return values.Min();
        if (p >= 1) return values.Max();

        var sorted = values.OrderBy(v => v).ToArray();
        var rank = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        var weight = rank - lower;
        return sorted[lower] + weight * (sorted[upper] - sorted[lower]);
    }

    /// <summary>
    /// Fraction of first-token latencies that would exceed <paramref name="budgetMs"/>
    /// — i.e. the cases where <see cref="LiveLingo.Core.Models.FallbackTranslationInvoker"/>
    /// would escalate to the next candidate in the route plan.
    /// Null entries (no measurable first-token, e.g. non-streaming) are ignored.
    /// </summary>
    public static double SimulatedFallbackRate(IEnumerable<long?> firstTokenLatencies, long budgetMs)
    {
        ArgumentNullException.ThrowIfNull(firstTokenLatencies);
        var measured = firstTokenLatencies.Where(l => l.HasValue).Select(l => l!.Value).ToArray();
        if (measured.Length == 0) return 0;
        return (double)measured.Count(l => l > budgetMs) / measured.Length;
    }
}
