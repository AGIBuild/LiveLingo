using System.Text;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>
/// Qwen3-ASR second pass: re-decodes one closed segment. Glossary terms are given as hotwords, which are part of the
/// loaded model. The loaded model is lent by <see cref="SherpaSpeechEngine"/>, to one user at a time (a dictation and a
/// file transcription never run together), and given back when this recognizer is disposed.
/// </summary>
public sealed class SherpaQwen3Recognizer(ResidentModel<OfflineRecognizer>.Lease model) : ISegmentRecognizer
{
    /// <summary>
    /// Qwen3-ASR 0.6B emits 25 audio tokens/s inside a 512-token total KV window (prompt + audio + generated text).
    /// Slicing stretches longer than 13 s (~325 audio tokens) at their quietest frame prevents native KV overflow
    /// (GenerateText:913) and avoids losing the tail of uninterrupted fast speech.
    /// </summary>
    public const int MaxChunkSamples = 13 * AudioFormat.SampleRate;

    private const int CutSearchSamples = 4 * AudioFormat.SampleRate;

    /// <param name="modelDir">Directory of the extracted <see cref="Models.ModelCatalog.SpeechSecondPass"/> model.</param>
    public static OfflineRecognizer Load(string modelDir, string hotwords, int threads = 4)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = AudioFormat.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        config.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(modelDir, "conv_frontend.onnx");
        config.ModelConfig.Qwen3Asr.Encoder = Path.Combine(modelDir, "encoder.int8.onnx");
        config.ModelConfig.Qwen3Asr.Decoder = Path.Combine(modelDir, "decoder.int8.onnx");
        config.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(modelDir, "tokenizer");
        config.ModelConfig.Qwen3Asr.Hotwords = hotwords;
        config.ModelConfig.Tokens = "";
        return new OfflineRecognizer(config);
    }

    public string Recognize(ReadOnlySpan<float> samples)
    {
        if (samples.Length <= MaxChunkSamples)
            return DecodeChunk(samples);

        var joined = new StringBuilder();
        var start = 0;
        while (samples.Length - start > MaxChunkSamples)
        {
            var cut = FileTranscriber.QuietestPoint(samples, start + MaxChunkSamples - CutSearchSamples, start + MaxChunkSamples);
            AppendChunk(joined, DecodeChunk(samples.Slice(start, cut - start)));
            start = cut;
        }
        AppendChunk(joined, DecodeChunk(samples[start..]));
        return joined.ToString();
    }

    private string DecodeChunk(ReadOnlySpan<float> chunk)
    {
        using var stream = model.Value.CreateStream();
        stream.AcceptWaveform(AudioFormat.SampleRate, chunk.ToArray());
        model.Value.Decode(stream);
        return stream.Result.Text.Trim();
    }

    private static void AppendChunk(StringBuilder target, string piece)
    {
        if (piece.Length == 0)
            return;
        if (target.Length > 0 && target[^1] == '。')
        {
            var core = target.ToString(0, target.Length - 1).TrimEnd();
            target.Length = core.Length;
            if (!DictationSession.EndsSeamlessMidPhrase(core))
                target.Append('，');
        }
        else if (target.Length > 0 && target[^1] == '.' && target.Length >= 2 && IsAsciiGlyph(target[^2]))
        {
            target.Length--;
        }

        if (target.Length > 0 && IsAsciiGlyph(target[^1]) && IsAsciiGlyph(piece[0]))
            target.Append(' ');
        target.Append(piece);
    }

    private static bool IsAsciiGlyph(char c) => c is > ' ' and < (char)127;

    public void Dispose() => model.Dispose();
}
