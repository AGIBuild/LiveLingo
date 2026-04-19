using LiveLingo.Core.Models;
using LiveLingo.Core.Speech;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LiveLingo.Core.Tests.Probes;

/// <summary>
/// Real-machine validation that the full sherpa-onnx pipeline can:
///   1. Resolve / download / extract the Cohere Transcribe 14-lang int8 archive
///   2. Construct an OfflineRecognizer through <see cref="SherpaCohereTranscribeEngine"/>
///   3. Decode a real wav file and return non-empty text
///
/// Skipped unless LIVELINGO_ENABLE_STT_PROBE=1; intended to be invoked through the
/// Nuke <c>ProbeStt</c> target so a developer can validate the full stack in one command.
/// </summary>
public sealed class SherpaSttProbeTests
{
    [Fact]
    [Trait("Category", "SttProbe")]
    public async Task SherpaCohereTranscribe_DecodesWav_ProducesExpectedText()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("LIVELINGO_ENABLE_STT_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var explicitWavPath = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_WAV_PATH");
        var expectedContains = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_EXPECTED_CONTAINS");
        var language = Environment.GetEnvironmentVariable("LIVELINGO_PROBE_LANG");
        var modelPath = ResolveModelPath();

        var coreOptions = new CoreOptions { ModelStoragePath = modelPath };
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
        var modelManager = new ModelManager(
            Options.Create(coreOptions), http, NullLogger<ModelManager>.Instance);

        var descriptor = ModelRegistry.SherpaCohereTranscribe14LangInt8;
        var lastReportedPercent = -1;
        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            if (p.TotalBytes <= 0) return;
            var percent = (int)(p.BytesDownloaded * 100 / p.TotalBytes);
            if (percent == lastReportedPercent) return;
            lastReportedPercent = percent;
            Console.WriteLine(
                $"[stt-probe] {descriptor.Id} {percent}% ({p.BytesDownloaded}/{p.TotalBytes})");
        });

        await modelManager.EnsureModelAsync(descriptor, progress, CancellationToken.None);
        Assert.True(
            modelManager.HasAllExpectedLocalAssets(descriptor),
            $"Model '{descriptor.Id}' did not install all expected files after EnsureModelAsync.");

        // Sherpa-onnx model bundles ship a `test_wavs/` directory next to the ONNX files.
        // When the caller didn't override the wav path, default to the English sample so
        // a fresh checkout can run the probe with zero extra inputs.
        var wavPath = explicitWavPath;
        if (string.IsNullOrWhiteSpace(wavPath))
        {
            var modelDir = modelManager.GetModelDirectory(descriptor.Id);
            wavPath = Path.Combine(modelDir, "test_wavs", "en.wav");
            language ??= "en";
        }
        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException(
                $"Probe wav not found: {wavPath}. Provide LIVELINGO_PROBE_WAV_PATH or " +
                "ensure the model bundle was extracted with its test_wavs/ directory.",
                wavPath);
        }

        using var engine = new SherpaCohereTranscribeEngine(
            modelManager, coreOptions, NullLogger<SherpaCohereTranscribeEngine>.Instance);

        var audio = LoadWavAsCaptureResult(wavPath);
        Console.WriteLine(
            $"[stt-probe] decoding {wavPath} ({audio.Duration.TotalSeconds:F2}s, lang={language ?? "auto"})");
        var result = await engine.TranscribeAsync(audio, language, CancellationToken.None);

        Console.WriteLine($"[stt-probe] decoded text: \"{result.Text}\"");
        Assert.False(string.IsNullOrWhiteSpace(result.Text), "Decoded text was empty.");
        if (!string.IsNullOrWhiteSpace(expectedContains))
        {
            Assert.Contains(
                expectedContains, result.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Minimal RIFF / WAVE parser limited to the PCM16 mono 16 kHz format that the
    /// production audio capture pipeline uses. Probe-only — production code never
    /// loads files from disk, so we deliberately avoid pulling in a wav library.
    /// </summary>
    private static AudioCaptureResult LoadWavAsCaptureResult(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs);

        ExpectAscii(br, "RIFF");
        br.ReadInt32(); // riff size
        ExpectAscii(br, "WAVE");

        short audioFormat = 0;
        short numChannels = 0;
        int sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? pcm = null;

        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadInt32();
            if (chunkId == "fmt ")
            {
                audioFormat = br.ReadInt16();
                numChannels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32(); // byte rate
                br.ReadInt16(); // block align
                bitsPerSample = br.ReadInt16();
                if (chunkSize > 16)
                    br.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                pcm = br.ReadBytes(chunkSize);
                break;
            }
            else
            {
                br.ReadBytes(chunkSize);
            }
        }

        if (pcm is null)
            throw new InvalidDataException($"WAV file '{wavPath}' has no data chunk.");
        if (audioFormat != 1 || bitsPerSample != 16 || numChannels != 1)
        {
            throw new NotSupportedException(
                $"Probe wav must be PCM16 mono (got format={audioFormat}, channels={numChannels}, bits={bitsPerSample}).");
        }

        var duration = TimeSpan.FromSeconds((double)pcm.Length / 2 / sampleRate);
        return new AudioCaptureResult(pcm, sampleRate, 1, duration);
    }

    private static void ExpectAscii(BinaryReader br, string expected)
    {
        var actual = new string(br.ReadChars(expected.Length));
        if (actual != expected)
            throw new InvalidDataException($"Expected '{expected}' marker in wav, got '{actual}'.");
    }

    private static string ResolveModelPath() =>
        Environment.GetEnvironmentVariable("LIVELINGO_PROBE_MODEL_PATH")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLingo",
            "models");
}
