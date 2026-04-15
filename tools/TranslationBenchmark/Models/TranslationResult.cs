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
    string? Error = null
)
{
    public bool IsSuccess => Error is null;
}

public sealed record ModelBenchmarkResult(
    string ModelName,
    string LanguagePair,
    IReadOnlyList<CaseResult> Cases
)
{
    public double AvgBleu => Cases.Where(c => c.IsSuccess).Select(c => c.BleuScore).DefaultIfEmpty(0).Average();
    public double? AvgJudge => Cases.Where(c => c.IsSuccess && c.JudgeScore.HasValue).Select(c => c.JudgeScore!.Value) is { } scores && scores.Any()
        ? scores.Average()
        : null;
    public double AvgElapsedMs => Cases.Where(c => c.IsSuccess).Select(c => (double)c.ElapsedMs).DefaultIfEmpty(0).Average();
    public int SuccessCount => Cases.Count(c => c.IsSuccess);
    public int TotalCount => Cases.Count;
}
