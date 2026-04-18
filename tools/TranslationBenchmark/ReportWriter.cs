using System.Text;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

public static class ReportWriter
{
    /// <summary>
    /// First-token budgets (ms) used to simulate production UsedFallbackRate.
    /// Matches the defaults in <c>LiveLingo.Core.Models.TranslationRoutePlanBuilder</c>
    /// so report numbers are directly comparable to telemetry output.
    /// </summary>
    public static readonly int[] SimulatedFirstTokenBudgetsMs = [400, 700, 1200];

    public static string BuildMarkdown(
        IReadOnlyList<ModelBenchmarkResult> results,
        DateTimeOffset runAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Translation Quality Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {runAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();

        WriteSummary(sb, results);
        WriteVariantComparison(sb, results);
        WriteLatencyBreakdown(sb, results);
        WriteSimulatedFallback(sb, results);
        WritePerPairDetail(sb, results);

        return sb.ToString();
    }

    private static void WriteSummary(StringBuilder sb, IReadOnlyList<ModelBenchmarkResult> results)
    {
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Model | Variant | Pair | BLEU | Judge | Avg ms | P95 ms | OK/Total |");
        sb.AppendLine("|-------|---------|------|-----:|------:|-------:|-------:|---------:|");
        foreach (var r in results)
        {
            var judge = r.AvgJudge.HasValue ? $"{r.AvgJudge.Value:F1}" : "—";
            sb.AppendLine(
                $"| {r.ModelName} | {r.PromptVariant} | {r.LanguagePair} | {r.AvgBleu:F3} | {judge} | " +
                $"{r.AvgElapsedMs:F0} | {r.P95ElapsedMs:F0} | {r.SuccessCount}/{r.TotalCount} |");
        }
        sb.AppendLine();
    }

    private static void WriteVariantComparison(StringBuilder sb, IReadOnlyList<ModelBenchmarkResult> results)
    {
        var groups = results
            .GroupBy(r => new { r.ModelName, r.PromptVariant })
            .Select(g => new
            {
                g.Key.ModelName,
                g.Key.PromptVariant,
                Bleu = g.SelectMany(r => r.Cases.Where(c => c.IsSuccess)).Select(c => c.BleuScore).DefaultIfEmpty(0).Average(),
                AvgMs = g.SelectMany(r => r.Cases.Where(c => c.IsSuccess)).Select(c => (double)c.ElapsedMs).DefaultIfEmpty(0).Average(),
                P95Ms = LatencyStats.Percentile(
                    g.SelectMany(r => r.Cases.Where(c => c.IsSuccess).Select(c => c.ElapsedMs)).ToArray(),
                    0.95),
                JudgeSamples = g.SelectMany(r => r.Cases).Where(c => c.JudgeScore.HasValue).Select(c => c.JudgeScore!.Value).ToArray(),
                Samples = g.Sum(r => r.SuccessCount)
            })
            .OrderBy(x => x.ModelName)
            .ThenBy(x => x.PromptVariant)
            .ToList();

        if (groups.Count <= 1) return;

        sb.AppendLine("## Prompt Variant Comparison (aggregated across pairs)");
        sb.AppendLine();
        sb.AppendLine("| Model | Variant | BLEU | Judge | Avg ms | P95 ms | Samples |");
        sb.AppendLine("|-------|---------|-----:|------:|-------:|-------:|--------:|");
        foreach (var g in groups)
        {
            var judge = g.JudgeSamples.Length > 0 ? $"{g.JudgeSamples.Average():F1}" : "—";
            sb.AppendLine(
                $"| {g.ModelName} | {g.PromptVariant} | {g.Bleu:F3} | {judge} | " +
                $"{g.AvgMs:F0} | {g.P95Ms:F0} | {g.Samples} |");
        }
        sb.AppendLine();
    }

    private static void WriteLatencyBreakdown(StringBuilder sb, IReadOnlyList<ModelBenchmarkResult> results)
    {
        if (!results.Any(r => r.HasFirstTokenSamples)) return;

        sb.AppendLine("## First-Token Latency (streaming runs only)");
        sb.AppendLine();
        sb.AppendLine("| Model | Variant | Pair | ttf P50 | ttf P95 | ttf P99 | total P95 |");
        sb.AppendLine("|-------|---------|------|--------:|--------:|--------:|----------:|");
        foreach (var r in results.Where(r => r.HasFirstTokenSamples))
        {
            sb.AppendLine(
                $"| {r.ModelName} | {r.PromptVariant} | {r.LanguagePair} | " +
                $"{r.P50FirstTokenMs:F0} | {r.P95FirstTokenMs:F0} | {r.P99FirstTokenMs:F0} | {r.P95ElapsedMs:F0} |");
        }
        sb.AppendLine();
    }

    private static void WriteSimulatedFallback(StringBuilder sb, IReadOnlyList<ModelBenchmarkResult> results)
    {
        var streamingResults = results.Where(r => r.HasFirstTokenSamples).ToList();
        if (streamingResults.Count == 0) return;

        sb.AppendLine("## Simulated UsedFallbackRate");
        sb.AppendLine();
        sb.AppendLine(
            "Fraction of streaming calls whose first-token latency would exceed a given " +
            "`FirstTokenBudget`, which is what the production `FallbackTranslationInvoker` " +
            "uses to escalate to the next candidate. Lower is better.");
        sb.AppendLine();

        var header = new StringBuilder("| Model | Variant | Pair |");
        var separator = new StringBuilder("|-------|---------|------|");
        foreach (var budget in SimulatedFirstTokenBudgetsMs)
        {
            header.Append($" >{budget}ms |");
            separator.Append("------:|");
        }
        sb.AppendLine(header.ToString());
        sb.AppendLine(separator.ToString());

        foreach (var r in streamingResults)
        {
            var row = new StringBuilder($"| {r.ModelName} | {r.PromptVariant} | {r.LanguagePair} |");
            foreach (var budget in SimulatedFirstTokenBudgetsMs)
                row.Append($" {r.SimulatedFallbackRate(budget):P0} |");
            sb.AppendLine(row.ToString());
        }
        sb.AppendLine();
    }

    private static void WritePerPairDetail(StringBuilder sb, IReadOnlyList<ModelBenchmarkResult> results)
    {
        var pairs = results.Select(r => r.LanguagePair).Distinct().ToList();
        foreach (var pair in pairs)
        {
            sb.AppendLine($"## {pair}");
            sb.AppendLine();

            foreach (var mr in results.Where(r => r.LanguagePair == pair))
            {
                sb.AppendLine($"### {mr.ModelName} · {mr.PromptVariant}{(mr.HasFirstTokenSamples ? " · streaming" : "")}");
                sb.AppendLine();
                sb.AppendLine("| ID | Domain | Source | Translation | Reference | BLEU | Judge | ms | ttf |");
                sb.AppendLine("|----|--------|--------|-------------|-----------|-----:|------:|---:|----:|");

                foreach (var c in mr.Cases)
                {
                    if (!c.IsSuccess)
                    {
                        sb.AppendLine($"| {c.CaseId} | {c.Domain} | {Escape(c.Source)} | **ERROR**: {Escape(c.Error!)} | — | — | — | — | — |");
                        continue;
                    }
                    var judge = c.JudgeScore.HasValue ? $"{c.JudgeScore.Value:F0}" : "—";
                    var ttf = c.FirstTokenMs.HasValue ? $"{c.FirstTokenMs.Value}" : "—";
                    sb.AppendLine(
                        $"| {c.CaseId} | {c.Domain} | {Escape(c.Source)} | {Escape(c.Translation)} | " +
                        $"{Escape(c.Reference)} | {c.BleuScore:F3} | {judge} | {c.ElapsedMs} | {ttf} |");
                }

                sb.AppendLine();
            }
        }
    }

    private static string Escape(string s) =>
        s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
