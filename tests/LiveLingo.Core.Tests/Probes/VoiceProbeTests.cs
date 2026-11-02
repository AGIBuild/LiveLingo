using System.Diagnostics;
using System.Text.Json;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// End-to-end probe of the voice chain (design spike S4): installs the pinned speech models, replays recorded audio
/// through the sherpa adapters, the dictation session and the cleanup scheduler, and reports text, CER and latency.
/// Skipped unless LIVELINGO_ENABLE_VOICE_PROBE=1; LIVELINGO_PROBE_CORPUS is a directory with scenarios.json and
/// audio/&lt;id&gt;.wav (16 kHz mono PCM16, see poc/voice-dictation); LIVELINGO_PROBE_SCENARIOS selects ids (comma separated);
/// LIVELINGO_PROBE_MAX_CER (optional) fails a scenario whose raw CER is higher. Compare against the POC results rather
/// than an absolute number: synthetic voices mispronounce English terms.
/// </summary>
public class VoiceProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProductPipeline_TranscribesRecordedSpeech()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_VOICE_PROBE") != "1")
            return;

        var corpus = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_CORPUS")!;
        var ids = (Environment.GetEnvironmentVariable("LIVELINGO_PROBE_SCENARIOS") ?? "terms-basic,self-correction").Split(',');
        var speedup = int.Parse(Environment.GetEnvironmentVariable("LIVELINGO_PROBE_SPEEDUP") ?? "4");

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
        foreach (var model in ModelCatalog.Speech.Append(ModelCatalog.LlamaRuntime).Append(ModelCatalog.Cleaner))
            await models.EnsureModelAsync(model);

        var scenarios = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "scenarios.json")))
            .RootElement.EnumerateObject().First().Value.EnumerateArray()
            .Where(e => e.TryGetProperty("parts", out _))
            .ToDictionary(e => e.GetProperty("id").GetString()!, e => string.Concat(
                e.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("say").GetString())));

        var factory = provider.GetRequiredService<IDictationSessionFactory>();
        var cleaner = provider.GetRequiredService<ITextCleaner>();
        foreach (var id in ids)
        {
            var samples = ReadMono16k(Path.Combine(corpus, "audio", id + ".wav"));
            var draft = new DraftSession(hasTarget: true);
            draft.Mutate(d => d.Output = OutputKind.Clean);
            await using var cleanup = new CleanupScheduler(draft, cleaner, [], contextChars: 300);
            await using var polish = new PolishScheduler(
                draft, provider.GetRequiredService<IDictationPolisher>(), [], PolishStyle.Default, contextChars: 300);

            var loading = Stopwatch.StartNew();
            var session = await factory.CreateAsync(draft, [], CancellationToken.None);
            output.WriteLine($"[{id}] models loaded in {loading.ElapsedMilliseconds} ms; audio {samples.Length / 16000.0:0.0} s");
            session.Start();

            var clock = Stopwatch.StartNew();
            for (var offset = 0; offset < samples.Length; offset += 640)
            {
                session.Push(samples[offset..Math.Min(samples.Length, offset + 640)]);
                await Task.Delay(40 / speedup);
            }

            var stopping = Stopwatch.StartNew();
            await session.DisposeAsync();
            var settled = stopping.ElapsedMilliseconds;
            while (draft.Read(d => d.Polishing || d.Units.Any(u => u.Clean.Text is null && u.Clean.Error.Length == 0)) && stopping.ElapsedMilliseconds < 120_000)
                await Task.Delay(50);
            Assert.False(draft.Read(d => d.Polishing), $"{id}: polish did not finish in time");
            var (raw, cleaned, warnings) = draft.Read(d => (
                d.SourceText(),
                TextUnits.Join(d.Units.Select(u => (u.Lead, u.Clean.Text ?? u.Raw, u.Trail))),
                string.Join(", ", d.Units.Where(u => u.Clean.Rejection is not null).Select(u => u.Clean.Rejection!.ToString()))));
            var spoken = scenarios[id];
            output.WriteLine($"[{id}] stop settled in {settled} ms, cleanup drained in {stopping.ElapsedMilliseconds} ms, replay {clock.ElapsedMilliseconds} ms");
            output.WriteLine($"[{id}] spoken : {spoken}");
            output.WriteLine($"[{id}] raw    : {raw}  (CER {Cer(spoken, raw):0.000})");
            output.WriteLine($"[{id}] cleaned: {cleaned}  (CER {Cer(spoken, cleaned):0.000}, gate: {(warnings.Length == 0 ? "accepted" : warnings)})");
            output.WriteLine($"[{id}] units  : {string.Join(" | ", draft.Read(d => d.Units.Select(u => u.Raw)))}");

            Assert.False(string.IsNullOrWhiteSpace(raw), $"{id}: nothing recognized");
            var maxCer = double.Parse(Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MAX_CER") ?? "1");
            Assert.True(Cer(spoken, raw) <= maxCer, $"{id}: raw CER {Cer(spoken, raw):0.000} above {maxCer}");
        }
    }

    private static float[] ReadMono16k(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var data = 12;
        while (data + 8 <= bytes.Length && System.Text.Encoding.ASCII.GetString(bytes, data, 4) != "data")
            data += 8 + BitConverter.ToInt32(bytes, data + 4);
        var start = data + 8;
        var samples = new float[(bytes.Length - start) / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, start + i * 2) / 32768f;
        return samples;
    }

    private static double Cer(string reference, string hypothesis)
    {
        static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var a = Norm(reference);
        var b = Norm(hypothesis);
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var current = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = current;
            }
        }
        return a.Length == 0 ? 0 : (double)row[b.Length] / a.Length;
    }
}
