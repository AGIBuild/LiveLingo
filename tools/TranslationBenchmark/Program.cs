using System.Text.Json;
using TranslationBenchmark;
using TranslationBenchmark.Models;

// ──────────────────────────────────────────────────────────────────────────────
// Translation Quality Benchmark
//
// Usage:
//   dotnet run -- [options]
//
// Options:
//   --config <path>     Path to benchmark-config.json (default: ./benchmark-config.json)
//   --output <path>     Output markdown report path (default: ./benchmark-report.md)
//   --verbose           Print per-sentence progress
//   --judge             Enable LLM-as-judge scoring (requires judge config in config file)
//
// Example benchmark-config.json:
//   {
//     "models": [
//       { "name": "Gemma4-12B", "baseUrl": "http://localhost:8080" },
//       { "name": "Qwen3.5-9B", "baseUrl": "http://localhost:8081" },
//       { "name": "GPT-4.1-mini", "baseUrl": "https://api.openai.com",
//         "apiKey": "sk-...", "modelId": "gpt-4.1-mini" }
//     ],
//     "judge": {
//       "name": "judge",
//       "baseUrl": "https://api.openai.com",
//       "apiKey": "sk-...",
//       "modelId": "gpt-4.1-mini"
//     }
//   }
// ──────────────────────────────────────────────────────────────────────────────

var configPath = GetArg(args, "--config") ?? "benchmark-config.json";
var outputPath = GetArg(args, "--output") ?? "benchmark-report.md";
var verbose = args.Contains("--verbose");
var useJudge = args.Contains("--judge");

Console.WriteLine("Translation Quality Benchmark");
Console.WriteLine("=".PadRight(50, '='));

if (!File.Exists(configPath))
{
    WriteDefaultConfig(configPath);
    Console.WriteLine($"No config found. Created template: {configPath}");
    Console.WriteLine("Edit the config file with your model endpoints, then re-run.");
    return 1;
}

var config = JsonSerializer.Deserialize<BenchmarkConfig>(
    await File.ReadAllTextAsync(configPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

if (config is null || config.Models.Count == 0)
{
    Console.Error.WriteLine("Config is empty or invalid. At least one model is required.");
    return 2;
}

Console.WriteLine($"Models : {string.Join(", ", config.Models.Select(m => m.Name))}");
Console.WriteLine($"Judge  : {(useJudge && config.Judge is not null ? config.Judge.Name : "disabled")}");
Console.WriteLine($"Output : {outputPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var runner = new BenchmarkRunner(
    config.Models,
    useJudge ? config.Judge : null,
    verbose);

var results = await runner.RunAsync(cts.Token);

var report = ReportWriter.BuildMarkdown(results, DateTimeOffset.Now);
await File.WriteAllTextAsync(outputPath, report, cts.Token);

Console.WriteLine($"\n✅ Report written to: {outputPath}");
PrintConsoleSummary(results);

return 0;

// ─────────────────── helpers ────────────────────────────────────────────────

static string? GetArg(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static void PrintConsoleSummary(IReadOnlyList<ModelBenchmarkResult> results)
{
    Console.WriteLine("\n── Aggregate BLEU by model ──────────────────────────");

    var modelNames = results.Select(r => r.ModelName).Distinct().ToList();
    foreach (var name in modelNames)
    {
        var modelResults = results.Where(r => r.ModelName == name).ToList();
        var overallBleu = modelResults.SelectMany(r => r.Cases.Where(c => c.IsSuccess))
                                      .Select(c => c.BleuScore).DefaultIfEmpty(0).Average();
        var overallJudge = modelResults.SelectMany(r => r.Cases.Where(c => c.JudgeScore.HasValue))
                                       .Select(c => c.JudgeScore!.Value);
        var judgeStr = overallJudge.Any() ? $"  Judge={overallJudge.Average():F1}/10" : "";
        Console.WriteLine($"  {name,-20} BLEU={overallBleu:F3}{judgeStr}");
    }

    Console.WriteLine();
}

static void WriteDefaultConfig(string path)
{
    var template = new BenchmarkConfig(
        Models:
        [
            new ModelEndpointConfig("Gemma4-12B-default",  "http://localhost:8080", PromptVariant: "default"),
            new ModelEndpointConfig("Gemma4-12B-gemma",    "http://localhost:8080", PromptVariant: "gemma"),
            new ModelEndpointConfig("Qwen3.5-9B",          "http://localhost:8081", PromptVariant: "default"),
            new ModelEndpointConfig("GPT-4.1-mini",        "https://api.openai.com",
                ApiKey: "YOUR_OPENAI_API_KEY", ModelId: "gpt-4.1-mini", PromptVariant: "default")
        ],
        Judge: new ModelEndpointConfig("gpt-4.1-mini", "https://api.openai.com",
            ApiKey: "YOUR_OPENAI_API_KEY", ModelId: "gpt-4.1-mini")
    );

    File.WriteAllText(path, JsonSerializer.Serialize(template,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}

public sealed record BenchmarkConfig(
    List<ModelEndpointConfig> Models,
    ModelEndpointConfig? Judge = null
);
