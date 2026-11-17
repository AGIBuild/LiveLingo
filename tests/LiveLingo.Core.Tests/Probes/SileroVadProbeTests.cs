using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Speech.Native;
using LiveLingo.Core.Transcription;
using Xunit.Abstractions;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>Installed-model checks, enabled only by LIVELINGO_ENABLE_VAD_PROBE=1. Never downloads models.</summary>
public class SileroVadProbeTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_VAD_PROBE") == "1";

    private static string ModelPath()
    {
        var storage = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH")
            ?? Path.Combine(UserData.Directory, "models");
        var path = Path.Combine(storage, ModelCatalog.SpeechVad.Id, "silero_vad.onnx");
        Assert.True(File.Exists(path), "Install the Silero VAD model before enabling the VAD probe.");
        return path;
    }

    [Fact]
    public void LargeSilentBatch_ExceedingNativeCapacity_ProducesNoSpeech()
    {
        if (!Enabled)
            return;

        using var vad = new SherpaSileroVad(ModelPath());
        vad.Accept(new float[125 * AudioFormat.SampleRate]);
        vad.Flush();

        Assert.False(vad.IsSpeechActive);
        Assert.False(vad.TryTake(out _));
        output.WriteLine("Processed one 125-second silent batch without speech.");
    }

    [Theory]
    [InlineData(640, 1f)]
    [InlineData(16000, 1f)]
    [InlineData(640, 15f)]
    public void LongSpeech_BoundsSpans_PreservesAbsoluteTimeAndDoesNotInventPauses(int frameSamples, float maxSpeechSeconds)
    {
        if (!Enabled)
            return;

        var path = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_AUDIO");
        Assert.True(File.Exists(path), "Set LIVELINGO_PROBE_AUDIO to a spoken 16-kHz mono WAV file.");
        var audio = WavReader.ReadFile(path!);
        Assert.True(audio.Length >= AudioFormat.SampleRate, "At least one second of speech is required.");
        using var vad = new SherpaSileroVad(ModelPath(), maxSpeechSeconds: maxSpeechSeconds);
        var spans = new List<SpeechSpan>();
        var continuedAtBoundary = false;
        long accepted = 0;
        while (accepted < 125L * AudioFormat.SampleRate)
        {
            for (var start = 0; start < audio.Length; start += frameSamples)
            {
                var count = Math.Min(frameSamples, audio.Length - start);
                vad.Accept(audio.AsSpan(start, count));
                accepted += count;
                while (vad.TryTake(out var span))
                {
                    spans.Add(span);
                    if (vad.IsSpeechActive)
                    {
                        continuedAtBoundary = true;
                        // A partial window is not evidence that speech stopped.
                        vad.Accept(new float[1]);
                        accepted++;
                        Assert.True(vad.IsSpeechActive);
                    }
                }
            }
        }
        vad.Flush();
        while (vad.TryTake(out var tail))
            spans.Add(tail);

        Assert.NotEmpty(spans);
        if (maxSpeechSeconds == 1f)
            Assert.True(continuedAtBoundary, "A safety flush must not be reported as a silence pause.");
        Assert.All(spans, span =>
        {
            Assert.True(span.EndSample > span.StartSample);
            Assert.InRange(span.EndSample, 1, accepted);
            Assert.InRange(span.EndSample - span.StartSample, 1, AudioFormat.MsToSamples((maxSpeechSeconds + 0.4) * 1000));
        });
        for (var i = 1; i < spans.Count; i++)
            Assert.True(spans[i].StartSample >= spans[i - 1].EndSample, "Safety flushes must not duplicate audio.");
        Assert.True(spans[^1].EndSample > 120L * AudioFormat.SampleRate, "Native flush must not reset absolute timestamps.");
        Assert.False(vad.IsSpeechActive);
        output.WriteLine($"Processed {accepted / (double)AudioFormat.SampleRate:F1}s in {frameSamples}-sample frames; " +
            $"{spans.Count} spans, longest {spans.Max(s => s.EndSample - s.StartSample) / (double)AudioFormat.SampleRate:F3}s.");
    }
}
