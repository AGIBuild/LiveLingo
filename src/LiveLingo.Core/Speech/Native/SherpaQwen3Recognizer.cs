using LiveLingo.Core.Dictation;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>Qwen3-ASR second pass: re-decodes one closed segment. Glossary terms are given as hotwords.</summary>
public sealed class SherpaQwen3Recognizer : ISegmentRecognizer
{
    private readonly OfflineRecognizer _recognizer;

    /// <param name="modelDir">Directory of the extracted <see cref="Models.ModelCatalog.SpeechSecondPass"/> model.</param>
    public SherpaQwen3Recognizer(string modelDir, int threads = 4, string hotwords = "")
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
        _recognizer = new OfflineRecognizer(config);
    }

    public string Recognize(ReadOnlySpan<float> samples)
    {
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(AudioFormat.SampleRate, samples.ToArray());
        _recognizer.Decode(stream);
        return stream.Result.Text.Trim();
    }

    public void Dispose() => _recognizer.Dispose();
}
