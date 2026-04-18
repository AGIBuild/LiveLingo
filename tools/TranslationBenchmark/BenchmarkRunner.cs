using System.Reflection;
using System.Text.Json;
using TranslationBenchmark.Models;

namespace TranslationBenchmark;

public sealed class BenchmarkRunner
{
    private static readonly (string File, string SrcLang, string TgtLang, string PairLabel)[] DataFiles =
    [
        ("TranslationBenchmark.Data.benchmark-zh-en.json", "zh", "en", "zh→en"),
        ("TranslationBenchmark.Data.benchmark-en-zh.json", "en", "zh", "en→zh"),
        ("TranslationBenchmark.Data.benchmark-ja-en.json", "ja", "en", "ja→en")
    ];

    private readonly IReadOnlyList<ModelEndpointConfig> _models;
    private readonly ModelEndpointConfig? _judgeConfig;
    private readonly bool _verbose;
    private readonly int _warmup;

    public BenchmarkRunner(
        IReadOnlyList<ModelEndpointConfig> models,
        ModelEndpointConfig? judgeConfig,
        bool verbose = false,
        int warmup = 0)
    {
        _models = models;
        _judgeConfig = judgeConfig;
        _verbose = verbose;
        _warmup = Math.Max(0, warmup);
    }

    public async Task<IReadOnlyList<ModelBenchmarkResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<ModelBenchmarkResult>();

        foreach (var (resourceName, srcLang, tgtLang, pairLabel) in DataFiles)
        {
            var cases = LoadEmbeddedCases(resourceName);
            Console.WriteLine($"\n=== {pairLabel} ({cases.Count} cases) ===");

            foreach (var modelConfig in _models)
            {
                if (ct.IsCancellationRequested) break;
                var header = $"{modelConfig.Name} · {modelConfig.PromptVariant}{(modelConfig.Streaming ? " · stream" : "")}";
                Console.WriteLine($"\n  Model: {header}");

                var caseResults = await RunModelOnCasesAsync(
                    modelConfig, cases, srcLang, tgtLang, ct).ConfigureAwait(false);

                results.Add(new ModelBenchmarkResult(
                    modelConfig.Name, modelConfig.PromptVariant, pairLabel, caseResults));

                PrintModelSummary(caseResults, cases.Count);
            }
        }

        return results;
    }

    private async Task<List<CaseResult>> RunModelOnCasesAsync(
        ModelEndpointConfig config, List<BenchmarkCase> cases,
        string srcLang, string tgtLang, CancellationToken ct)
    {
        var results = new List<CaseResult>();
        using var client = new TranslationClient(config);
        using var judge = _judgeConfig is not null ? new LlmJudge(_judgeConfig) : null;

        // Warm-up: run first N cases without recording, to shed JIT / model-load noise.
        for (var w = 0; w < Math.Min(_warmup, cases.Count); w++)
        {
            if (ct.IsCancellationRequested) return results;
            try
            {
                _ = await client.TranslateAsync(cases[w].Source, srcLang, tgtLang, ct).ConfigureAwait(false);
                if (_verbose)
                    Console.WriteLine($"    [warmup {w + 1}/{_warmup}] ok");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"    [warmup {w + 1}/{_warmup}] {ex.Message}");
            }
        }

        foreach (var c in cases)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (_verbose)
                    Console.Write($"    [{c.Id}] translating... ");

                var call = await client.TranslateAsync(c.Source, srcLang, tgtLang, ct)
                    .ConfigureAwait(false);

                var bleu = BleuScorer.Score(call.Translation, c.Reference);
                double? judgeScore = null;

                if (judge is not null)
                    judgeScore = await judge.JudgeAsync(c.Source, call.Translation, srcLang, tgtLang, ct)
                        .ConfigureAwait(false);

                results.Add(new CaseResult(c.Id, c.Domain, c.Source, c.Reference,
                    call.Translation, bleu, judgeScore, call.ElapsedMs, call.FirstTokenMs));

                if (_verbose)
                {
                    var ftt = call.FirstTokenMs is { } ft ? $" ttf={ft}ms" : "";
                    Console.WriteLine($"BLEU={bleu:F3}{(judgeScore.HasValue ? $" Judge={judgeScore:F1}" : "")} ({call.ElapsedMs}ms{ftt})");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"    [{c.Id}] ERROR: {ex.Message}");
                results.Add(new CaseResult(c.Id, c.Domain, c.Source, c.Reference,
                    "", 0, null, 0, FirstTokenMs: null, Error: ex.Message));
            }
        }

        return results;
    }

    private static void PrintModelSummary(List<CaseResult> caseResults, int totalCases)
    {
        var ok = caseResults.Where(r => r.IsSuccess).ToList();
        if (ok.Count == 0)
        {
            Console.WriteLine($"    ✗ 0/{totalCases}");
            return;
        }

        var avgBleu = ok.Select(r => r.BleuScore).Average();
        var avgMs = ok.Select(r => (double)r.ElapsedMs).Average();
        var p95Ms = LatencyStats.Percentile([.. ok.Select(r => r.ElapsedMs)], 0.95);
        var ftts = ok.Where(r => r.FirstTokenMs.HasValue).Select(r => r.FirstTokenMs!.Value).ToArray();
        var ftSegment = ftts.Length > 0
            ? $" | ttf P50={LatencyStats.Percentile(ftts, 0.50):F0}ms P95={LatencyStats.Percentile(ftts, 0.95):F0}ms"
            : string.Empty;
        Console.WriteLine(
            $"    ✓ {ok.Count}/{totalCases} | BLEU={avgBleu:F3} | avg={avgMs:F0}ms P95={p95Ms:F0}ms{ftSegment}");
    }

    private static List<BenchmarkCase> LoadEmbeddedCases(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        return JsonSerializer.Deserialize<List<BenchmarkCase>>(stream) ?? [];
    }
}
