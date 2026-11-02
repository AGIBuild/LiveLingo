using Dictation.Core;
using SherpaOnnx;

namespace Dictation.Asr.Sherpa;

public sealed class SherpaOfflineRecognizer : ISegmentRecognizer
{
    private readonly OfflineRecognizer _recognizer;

    private SherpaOfflineRecognizer(OfflineRecognizerConfig config) => _recognizer = new OfflineRecognizer(config);

    public string Recognize(ReadOnlySpan<float> samples)
    {
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(AudioFormat.SampleRate, samples.ToArray());
        _recognizer.Decode(stream);
        return stream.Result.Text.Trim();
    }

    public void Dispose() => _recognizer.Dispose();

    private static OfflineRecognizerConfig NewConfig(int threads)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = AudioFormat.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        return config;
    }

    /// <summary>Offline mode of the X-ASR zipformer transducer (same family as the live model).</summary>
    public static SherpaOfflineRecognizer XAsr(string dir, int threads = 4)
    {
        var file = ModelFiles.In(dir);
        var config = NewConfig(threads);
        config.ModelConfig.Transducer.Encoder = file.First("encoder*.onnx");
        config.ModelConfig.Transducer.Decoder = file.First("decoder*.onnx");
        config.ModelConfig.Transducer.Joiner = file.First("joiner*.onnx");
        config.ModelConfig.Tokens = file.Exact("tokens.txt");
        return new SherpaOfflineRecognizer(config);
    }

    public static SherpaOfflineRecognizer Qwen3Asr(string dir, int threads = 4, string hotwords = "")
    {
        var file = ModelFiles.In(dir);
        var config = NewConfig(threads);
        config.ModelConfig.Qwen3Asr.ConvFrontend = file.Exact("conv_frontend.onnx");
        config.ModelConfig.Qwen3Asr.Encoder = file.Exact("encoder.int8.onnx");
        config.ModelConfig.Qwen3Asr.Decoder = file.Exact("decoder.int8.onnx");
        config.ModelConfig.Qwen3Asr.Tokenizer = file.Exact("tokenizer");
        config.ModelConfig.Qwen3Asr.Hotwords = hotwords;
        config.ModelConfig.Tokens = "";
        return new SherpaOfflineRecognizer(config);
    }

    public static SherpaOfflineRecognizer FunAsrNano(string dir, int threads = 4)
    {
        var file = ModelFiles.In(dir);
        var config = NewConfig(threads);
        config.ModelConfig.FunAsrNano.EncoderAdaptor = file.Exact("encoder_adaptor.int8.onnx");
        config.ModelConfig.FunAsrNano.LLM = file.Exact("llm.int8.onnx");
        config.ModelConfig.FunAsrNano.Embedding = file.Exact("embedding.int8.onnx");
        config.ModelConfig.FunAsrNano.Tokenizer = file.Exact("Qwen3-0.6B");
        config.ModelConfig.Tokens = "";
        return new SherpaOfflineRecognizer(config);
    }

    public static SherpaOfflineRecognizer SenseVoice(string dir, int threads = 4)
    {
        var file = ModelFiles.In(dir);
        var config = NewConfig(threads);
        config.ModelConfig.SenseVoice.Model = file.Exact("model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = "auto";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.Tokens = file.Exact("tokens.txt");
        return new SherpaOfflineRecognizer(config);
    }
}

internal readonly struct ModelFiles
{
    private readonly string _dir;
    private ModelFiles(string dir) => _dir = dir;

    public static ModelFiles In(string dir) =>
        Directory.Exists(dir) ? new ModelFiles(dir) : throw new DirectoryNotFoundException($"Model directory not found: {dir}");

    public string Exact(string name)
    {
        var path = Path.Combine(_dir, name);
        return File.Exists(path) || Directory.Exists(path) ? path : throw new FileNotFoundException(path);
    }

    /// <summary>Resolves a single file by pattern; ambiguity is an error, never a guess.</summary>
    public string First(string pattern) =>
        Directory.GetFiles(_dir, pattern) is [var only] ? only : throw new FileNotFoundException($"{pattern} in {_dir}");
}
