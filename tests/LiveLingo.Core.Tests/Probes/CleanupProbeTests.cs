using System.Text.Json;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// Parity probe (design spike S2): runs sentences recorded by the POC cleaner (LLamaSharp, same model and prompt)
/// through the llama-server cleaner and compares gate outcomes and outputs. Skipped unless
/// LIVELINGO_PROBE_CLEANUP_CASES points to a JSON array of { raw, continues, poc_state, poc_output }.
/// </summary>
public class CleanupProbeTests(ITestOutputHelper output)
{
    private sealed record Case(string Raw, bool Continues, string PocState, string? PocOutput);

    [Fact]
    public async Task LlamaServerCleaner_MatchesThePocCleaner()
    {
        var path = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CLEANUP_CASES");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var stride = int.Parse(Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CLEANUP_STRIDE") ?? "4");
        var cases = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement.EnumerateArray()
            .Select(e => new Case(
                e.GetProperty("raw").GetString()!,
                e.GetProperty("continues").GetBoolean(),
                e.GetProperty("poc_state").GetString()!,
                e.GetProperty("poc_output").GetString()))
            .Where((_, index) => index % stride == 0)
            .ToList();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(options =>
        {
            var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
            if (!string.IsNullOrWhiteSpace(storage))
                options.ModelStoragePath = storage;
        });
        await using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();
        await models.EnsureModelAsync(ModelCatalog.LlamaRuntime);
        await models.EnsureModelAsync(ModelCatalog.Cleaner);
        var cleaner = provider.GetRequiredService<ITextCleaner>();

        int accepted = 0, rejected = 0, exact = 0, bothAccepted = 0, gateAgrees = 0, failed = 0;
        var totalMs = 0.0;
        var disagreements = new List<string>();
        foreach (var testCase in cases)
        {
            var started = DateTime.UtcNow;
            string cleaned;
            try
            {
                cleaned = await cleaner.CleanAsync(new CleanupRequest(testCase.Raw, "", testCase.Continues, []), CancellationToken.None);
            }
            catch (Exception ex)
            {
                failed++;
                output.WriteLine($"FAILED: {testCase.Raw} ({ex.Message})");
                continue;
            }
            totalMs += (DateTime.UtcNow - started).TotalMilliseconds;

            var gate = FidelityGate.Check(testCase.Raw, cleaned);
            if (gate.Accepted) accepted++; else rejected++;
            var pocAccepted = testCase.PocState == "Cleaned";
            if (gate.Accepted == pocAccepted) gateAgrees++;
            if (gate.Accepted && pocAccepted)
            {
                bothAccepted++;
                if (cleaned == testCase.PocOutput) exact++;
                else disagreements.Add($"{testCase.Raw}\n    poc: {testCase.PocOutput}\n    srv: {cleaned}");
            }
        }

        var n = cases.Count - failed;
        output.WriteLine($"cases={cases.Count} failed={failed} accepted={accepted} rejected={rejected} " +
                         $"(poc rejected {cases.Count(c => c.PocState == "Rejected")}/{cases.Count}) " +
                         $"gate agrees={gateAgrees}/{n} identical when both accepted={exact}/{bothAccepted} " +
                         $"mean latency={totalMs / Math.Max(1, n):0} ms");
        foreach (var line in disagreements.Take(15))
            output.WriteLine(line);

        Assert.Equal(0, failed);
    }
}
