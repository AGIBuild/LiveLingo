using LiveLingo.Core.Dictation;
using LiveLingo.Core.Models;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>
/// Qwen3-ASR second pass: re-decodes one closed segment. Glossary terms are given as hotwords, which are part of the
/// loaded model. The loaded model is lent by <see cref="SherpaSpeechEngine"/>, to one user at a time (a dictation and a
/// file transcription never run together), and given back when this recognizer is disposed.
/// </summary>
public sealed class SherpaQwen3Recognizer(ResidentModel<OfflineRecognizer>.Lease model) : ISegmentRecognizer
{
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
        using var stream = model.Value.CreateStream();
        stream.AcceptWaveform(AudioFormat.SampleRate, samples.ToArray());
        model.Value.Decode(stream);
        return stream.Result.Text.Trim();
    }

    public void Dispose() => model.Dispose();
}
