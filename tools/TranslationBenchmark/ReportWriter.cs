using System.Text;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

public static class ReportWriter
{
    public static string BuildMarkdown(
        IReadOnlyList<ModelBenchmarkResult> results,
        DateTimeOffset runAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Translation Quality Benchmark Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {runAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Model | Pair | BLEU | Judge | Avg ms | OK/Total |");
        sb.AppendLine("|-------|------|-----:|------:|-------:|---------:|");
        foreach (var r in results)
        {
            var judge = r.AvgJudge.HasValue ? $"{r.AvgJudge.Value:F1}" : "—";
            sb.AppendLine($"| {r.ModelName} | {r.LanguagePair} | {r.AvgBleu:F3} | {judge} | {r.AvgElapsedMs:F0} | {r.SuccessCount}/{r.TotalCount} |");
        }

        sb.AppendLine();

        // Per-model breakdown per pair
        var pairs = results.Select(r => r.LanguagePair).Distinct().ToList();
        foreach (var pair in pairs)
        {
            sb.AppendLine($"## {pair}");
            sb.AppendLine();

            var pairResults = results.Where(r => r.LanguagePair == pair).ToList();

            foreach (var mr in pairResults)
            {
                sb.AppendLine($"### {mr.ModelName}");
                sb.AppendLine();
                sb.AppendLine("| ID | Domain | Source | Translation | Reference | BLEU | Judge | ms |");
                sb.AppendLine("|----|--------|--------|-------------|-----------|-----:|------:|---:|");

                foreach (var c in mr.Cases)
                {
                    if (!c.IsSuccess)
                    {
                        sb.AppendLine($"| {c.CaseId} | {c.Domain} | {Escape(c.Source)} | **ERROR**: {Escape(c.Error!)} | — | — | — | — |");
                        continue;
                    }
                    var judge = c.JudgeScore.HasValue ? $"{c.JudgeScore.Value:F0}" : "—";
                    sb.AppendLine($"| {c.CaseId} | {c.Domain} | {Escape(c.Source)} | {Escape(c.Translation)} | {Escape(c.Reference)} | {c.BleuScore:F3} | {judge} | {c.ElapsedMs} |");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
