using System.Text;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>
/// Offline second-pass recognizer: re-decodes one closed segment using the active <see cref="OfflineRecognizer"/>
/// and its <see cref="SpeechOptimizationProfile"/> (Qwen3-ASR 1.7B, Qwen3-ASR 0.6B, or lightweight X-ASR Offline transducer).
/// The loaded model is lent by <see cref="SherpaSpeechEngine"/> to one user at a time and returned when disposed.
/// </summary>
public sealed class SherpaQwen3Recognizer(
    ResidentModel<OfflineRecognizer>.Lease model,
    SpeechOptimizationProfile? profile = null) : ISegmentRecognizer
{
    public const int MaxChunkSamples = 13 * AudioFormat.SampleRate;

    private const int CutSearchSamples = 4 * AudioFormat.SampleRate;

    private readonly SpeechOptimizationProfile _profile =
        profile ?? SpeechOptimizationStrategies.ForModel(ModelCatalog.SpeechSecondPass.Id).Profile;

    public int EffectiveMaxChunkSamples =>
        _profile.MaxSegmentSeconds > 0 ? _profile.MaxSegmentSeconds * AudioFormat.SampleRate : MaxChunkSamples;

    /// <param name="modelDir">Directory of the extracted Qwen3-ASR ONNX model (<c>0.6B</c> or <c>1.7B</c>).</param>
    public static OfflineRecognizer Load(string modelDir, string hotwords, int threads = 4, int maxNewTokens = 192)
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
        config.ModelConfig.Qwen3Asr.MaxTotalLen = 512;
        config.ModelConfig.Qwen3Asr.MaxNewTokens = maxNewTokens;
        config.ModelConfig.Qwen3Asr.Hotwords = hotwords;
        config.ModelConfig.Tokens = "";
        return new OfflineRecognizer(config);
    }

    public string Recognize(ReadOnlySpan<float> samples)
    {
        var maxChunkSamples = EffectiveMaxChunkSamples;
        if (samples.Length <= maxChunkSamples)
            return DecodeChunk(samples);

        var joined = new StringBuilder();
        var start = 0;
        while (samples.Length - start > maxChunkSamples)
        {
            var searchWindow = Math.Min(CutSearchSamples, maxChunkSamples / 2);
            var cut = FileTranscriber.QuietestPoint(samples, start + maxChunkSamples - searchWindow, start + maxChunkSamples);
            AppendChunk(joined, DecodeChunk(samples.Slice(start, cut - start)), _profile.StripChunkBoundaryPeriod);
            start = cut;
        }
        AppendChunk(joined, DecodeChunk(samples[start..]), _profile.StripChunkBoundaryPeriod);
        return joined.ToString();
    }

    private string DecodeChunk(ReadOnlySpan<float> chunk)
    {
        using var stream = model.Value.CreateStream();
        stream.AcceptWaveform(AudioFormat.SampleRate, chunk.ToArray());
        model.Value.Decode(stream);
        return TextTidy.Apply(stream.Result.Text);
    }

    internal static void AppendChunk(StringBuilder target, string piece, bool stripBoundaryPeriod = true)
    {
        if (piece.Length == 0)
            return;
        if (stripBoundaryPeriod && target.Length > 0 && target[^1] == '。')
        {
            var core = target.ToString(0, target.Length - 1).TrimEnd();
            target.Length = core.Length;
            if (!DictationSession.EndsSeamlessMidPhrase(core))
                target.Append('，');
        }
        else if (stripBoundaryPeriod && target.Length > 0 && target[^1] == '.' && target.Length >= 2 && IsAsciiGlyph(target[^2]))
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
