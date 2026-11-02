using System.Diagnostics;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// Design spike S1: the memory of everything resident at once (live and second-pass recognizers, cleaner, translator).
/// Loads all of it, runs one request through each, then adds up the resident memory of this process and its
/// llama-server children (macOS only: it reads <c>ps</c>). Skipped unless LIVELINGO_ENABLE_VOICE_PROBE=1;
/// LIVELINGO_PROBE_AUDIO is a 16 kHz WAV used to warm the recognizers; LIVELINGO_PROBE_TIER=Quality uses the 7B translator.
/// It reports a footprint on the machine it runs on; it does not replace a run on the real 16 GB target.
/// </summary>
public class ResidentMemoryProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EverythingResident_ReportsTheTotalFootprint()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_VOICE_PROBE") != "1" || !OperatingSystem.IsMacOS())
            return;

        var tier = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_TIER") == "Quality" ? TranslatorTier.Quality : TranslatorTier.Fast;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore(options =>
        {
            var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH");
            if (!string.IsNullOrWhiteSpace(storage))
                options.ModelStoragePath = storage;
            options.TranslatorTier = tier;
        });
        await using var provider = services.BuildServiceProvider();
        var models = provider.GetRequiredService<IModelManager>();
        foreach (var model in ModelCatalog.Speech.Append(ModelCatalog.LlamaRuntime).Append(ModelCatalog.Cleaner).Append(ModelCatalog.Translator(tier)))
            await models.EnsureModelAsync(model);

        var self = Process.GetCurrentProcess();
        output.WriteLine($"baseline: {Mb(self.WorkingSet64)} MB");

        var translation = await provider.GetRequiredService<ITranslator>().TranslateAsync(
            new TranslationRequest("这个 PR 先不要合并，等 CI 跑完。", TargetLanguage.English, []), CancellationToken.None);
        var cleaned = await provider.GetRequiredService<ITextCleaner>().CleanAsync(
            new CleanupRequest("呃那个我们先把缓存的那个问题处理一下吧", "", false, []), CancellationToken.None);
        output.WriteLine($"translator ({tier}) answered '{translation.Text}' in {translation.TotalMs:0} ms; cleaner answered '{cleaned}'");

        var draft = new DraftSession(hasTarget: true);
        var session = await provider.GetRequiredService<IDictationSessionFactory>().CreateAsync(draft, [], CancellationToken.None);
        session.Start();
        var samples = WavReader.ReadFile(Environment.GetEnvironmentVariable("LIVELINGO_PROBE_AUDIO")!);
        for (var offset = 0; offset < Math.Min(samples.Length, 20 * 16000); offset += 640)
            session.Push(samples[offset..Math.Min(samples.Length, offset + 640)]);
        await Task.Delay(3000);

        // Measured while the session still holds the speech models.
        self.Refresh();
        var children = Children(self.Id);
        var total = self.WorkingSet64 + children.Sum(c => c.RssBytes);
        output.WriteLine($"app process (speech models loaded): {Mb(self.WorkingSet64)} MB");
        foreach (var child in children)
            output.WriteLine($"{child.Name} (pid {child.Pid}): {Mb(child.RssBytes)} MB");
        output.WriteLine($"TOTAL resident: {Mb(total)} MB (tier {tier})");
        await session.DisposeAsync();
        Assert.NotEmpty(children);
    }

    private static long Mb(long bytes) => bytes / 1_000_000;

    private static List<(int Pid, string Name, long RssBytes)> Children(int parent)
    {
        using var ps = Process.Start(new ProcessStartInfo("ps", "-axo pid=,ppid=,rss=,comm=") { RedirectStandardOutput = true })!;
        var text = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        var result = new List<(int, string, long)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && int.Parse(parts[1]) == parent)
                result.Add((int.Parse(parts[0]), Path.GetFileName(parts[3]), long.Parse(parts[2]) * 1024));
        }
        return result;
    }
}
