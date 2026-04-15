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

    public BenchmarkRunner(
        IReadOnlyList<ModelEndpointConfig> models,
        ModelEndpointConfig? judgeConfig,
        bool verbose = false)
    {
        _models = models;
        _judgeConfig = judgeConfig;
        _verbose = verbose;
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
                Console.WriteLine($"\n  Model: {modelConfig.Name}");

                var caseResults = await RunModelOnCasesAsync(
                    modelConfig, cases, srcLang, tgtLang, ct).ConfigureAwait(false);

                results.Add(new ModelBenchmarkResult(modelConfig.Name, pairLabel, caseResults));

                var success = caseResults.Count(r => r.IsSuccess);
                var avgBleu = caseResults.Where(r => r.IsSuccess).Select(r => r.BleuScore).DefaultIfEmpty(0).Average();
                Console.WriteLine($"    ✓ {success}/{cases.Count} | avg BLEU = {avgBleu:F3} | avg {caseResults.Where(r => r.IsSuccess).Select(r => (double)r.ElapsedMs).DefaultIfEmpty(0).Average():F0}ms");
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

        foreach (var c in cases)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (_verbose)
                    Console.Write($"    [{c.Id}] translating... ");

                var (translation, elapsed) = await client.TranslateAsync(c.Source, srcLang, tgtLang, ct)
                    .ConfigureAwait(false);

                var bleu = BleuScorer.Score(translation, c.Reference);
                double? judgeScore = null;

                if (judge is not null)
                    judgeScore = await judge.JudgeAsync(c.Source, translation, srcLang, tgtLang, ct)
                        .ConfigureAwait(false);

                results.Add(new CaseResult(c.Id, c.Domain, c.Source, c.Reference,
                    translation, bleu, judgeScore, elapsed));

                if (_verbose)
                    Console.WriteLine($"BLEU={bleu:F3}{(judgeScore.HasValue ? $" Judge={judgeScore:F1}" : "")} ({elapsed}ms)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"    [{c.Id}] ERROR: {ex.Message}");
                results.Add(new CaseResult(c.Id, c.Domain, c.Source, c.Reference,
                    "", 0, null, 0, ex.Message));
            }
        }

        return results;
    }

    private static List<BenchmarkCase> LoadEmbeddedCases(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        return JsonSerializer.Deserialize<List<BenchmarkCase>>(stream) ?? [];
    }
}
