using System.Diagnostics;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// End-to-end probe of importing a recording: installs the pinned speech models, transcribes a WAV file with the
/// product adapters and prints the timed segments and the real-time factor. Skipped unless
/// LIVELINGO_ENABLE_VOICE_PROBE=1; LIVELINGO_PROBE_AUDIO is the WAV file (a long-form recording from the corpus works).
/// </summary>
public class TranscriptionProbeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProductPipeline_TranscribesARecordingWithTimestamps()
    {
        if (Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_VOICE_PROBE") != "1")
            return;

        var path = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_AUDIO")!;
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
        foreach (var model in ModelCatalog.Transcription)
            await models.EnsureModelAsync(model);

        var seconds = WavReader.ReadFile(path).Length / 16000.0;
        var clock = Stopwatch.StartNew();
        var segments = await provider.GetRequiredService<IAudioFileTranscriber>()
            .TranscribeAsync(path, [], null, CancellationToken.None);

        output.WriteLine($"audio {seconds:0.0} s, transcribed in {clock.Elapsed.TotalSeconds:0.0} s (x{seconds / clock.Elapsed.TotalSeconds:0.0} real time), {segments.Count} segments");
        output.WriteLine(SrtWriter.Write(segments));
        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.True(s.End > s.Start));
    }
}
