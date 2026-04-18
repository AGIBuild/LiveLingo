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
//   --warmup <N>        Drop the first N cases per model from recorded stats (default: 2)
//   --verbose           Print per-sentence progress
//   --judge             Enable LLM-as-judge scoring (requires judge config in config file)
//
// Example benchmark-config.json (streaming + prompt-variant sweep for Gemma 4):
//   {
//     "models": [
//       { "name": "Gemma4-26B-A4B",  "baseUrl": "http://localhost:8080", "streaming": true, "promptVariant": "default" },
//       { "name": "Gemma4-26B-A4B",  "baseUrl": "http://localhost:8080", "streaming": true, "promptVariant": "gemma4-tagged" },
//       { "name": "Gemma4-26B-A4B",  "baseUrl": "http://localhost:8080", "streaming": true, "promptVariant": "gemma4-concise" },
//       { "name": "Gemma4-26B-A4B",  "baseUrl": "http://localhost:8080", "streaming": true, "promptVariant": "gemma4-structured" },
//       { "name": "Gemma4-E4B",      "baseUrl": "http://localhost:8081", "streaming": true, "promptVariant": "gemma4-concise" }
//     ],
//     "judge": {
//       "name": "judge", "baseUrl": "https://api.openai.com",
//       "apiKey": "sk-...", "modelId": "gpt-4.1-mini"
//     }
//   }
// ──────────────────────────────────────────────────────────────────────────────

var configPath = GetArg(args, "--config") ?? "benchmark-config.json";
var outputPath = GetArg(args, "--output") ?? "benchmark-report.md";
var warmup = int.TryParse(GetArg(args, "--warmup"), out var w) ? w : 2;
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

Console.WriteLine($"Models : {string.Join(", ", config.Models.Select(m => $"{m.Name}/{m.PromptVariant}{(m.Streaming ? "/stream" : "")}"))}");
Console.WriteLine($"Judge  : {(useJudge && config.Judge is not null ? config.Judge.Name : "disabled")}");
Console.WriteLine($"Warmup : {warmup}");
Console.WriteLine($"Output : {outputPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var runner = new BenchmarkRunner(
    config.Models,
    useJudge ? config.Judge : null,
    verbose,
    warmup);

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
    Console.WriteLine("\n── Aggregate by (model, variant) ────────────────────");

    var groups = results
        .GroupBy(r => new { r.ModelName, r.PromptVariant })
        .OrderBy(g => g.Key.ModelName)
        .ThenBy(g => g.Key.PromptVariant);

    foreach (var group in groups)
    {
        var allOk = group.SelectMany(r => r.Cases.Where(c => c.IsSuccess)).ToList();
        if (allOk.Count == 0) continue;

        var bleu = allOk.Select(c => c.BleuScore).Average();
        var avgMs = allOk.Select(c => (double)c.ElapsedMs).Average();
        var p95Ms = LatencyStats.Percentile([.. allOk.Select(c => c.ElapsedMs)], 0.95);
        var judgeSamples = allOk.Where(c => c.JudgeScore.HasValue).Select(c => c.JudgeScore!.Value).ToArray();
        var judgeStr = judgeSamples.Length > 0 ? $"  Judge={judgeSamples.Average():F1}/10" : "";

        var ftts = allOk.Where(c => c.FirstTokenMs.HasValue).Select(c => c.FirstTokenMs!.Value).ToArray();
        var ftSegment = ftts.Length > 0
            ? $"  ttf P95={LatencyStats.Percentile(ftts, 0.95):F0}ms"
            : "";

        Console.WriteLine(
            $"  {group.Key.ModelName,-20} {group.Key.PromptVariant,-20} " +
            $"BLEU={bleu:F3}  avg={avgMs,5:F0}ms  P95={p95Ms,5:F0}ms{ftSegment}{judgeStr}");
    }
    Console.WriteLine();
}

static void WriteDefaultConfig(string path)
{
    var template = new BenchmarkConfig(
        Models:
        [
            new ModelEndpointConfig("Gemma4-26B-A4B", "http://localhost:8080", PromptVariant: "default",           Streaming: true),
            new ModelEndpointConfig("Gemma4-26B-A4B", "http://localhost:8080", PromptVariant: "gemma4-tagged",     Streaming: true),
            new ModelEndpointConfig("Gemma4-26B-A4B", "http://localhost:8080", PromptVariant: "gemma4-concise",    Streaming: true),
            new ModelEndpointConfig("Gemma4-26B-A4B", "http://localhost:8080", PromptVariant: "gemma4-structured", Streaming: true),
            new ModelEndpointConfig("Gemma4-E4B",     "http://localhost:8081", PromptVariant: "gemma4-concise",    Streaming: true),
            new ModelEndpointConfig("GPT-4.1-mini",   "https://api.openai.com",
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
