using Dictation.Core;
using SherpaOnnx;

namespace Dictation.Asr.Sherpa;

/// <summary>
/// Streaming zipformer transducer. One stream per acoustic segment: closing a segment pads the tail with silence,
/// so the final right-context is flushed, and starts a fresh stream.
/// </summary>
public sealed class SherpaLiveRecognizer : ILiveRecognizer
{
    private readonly OnlineRecognizer _recognizer;
    private readonly int _tailPaddingMs;
    private OnlineStream _stream;
    private string _text = "";

    public SherpaLiveRecognizer(string modelDir, int threads = 2, int tailPaddingMs = 800)
    {
        _tailPaddingMs = tailPaddingMs;
        var file = ModelFiles.In(modelDir);

        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = AudioFormat.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = file.First("encoder*.onnx");
        config.ModelConfig.Transducer.Decoder = file.First("decoder*.onnx");
        config.ModelConfig.Transducer.Joiner = file.First("joiner*.onnx");
        config.ModelConfig.Tokens = file.Exact("tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 0; // segment boundaries come from the voice activity detector, not from the recognizer

        _recognizer = new OnlineRecognizer(config);
        _stream = _recognizer.CreateStream();
    }

    public string CurrentText => _text;

    public void Accept(ReadOnlySpan<float> samples)
    {
        _stream.AcceptWaveform(AudioFormat.SampleRate, samples.ToArray());
        DecodeReady();
        _text = _recognizer.GetResult(_stream).Text.Trim();
    }

    public string CloseSegment()
    {
        _stream.AcceptWaveform(AudioFormat.SampleRate, new float[AudioFormat.MsToSamples(_tailPaddingMs)]);
        _stream.InputFinished();
        DecodeReady();
        var text = _recognizer.GetResult(_stream).Text.Trim();

        _stream.Dispose();
        _stream = _recognizer.CreateStream();
        _text = "";
        return text;
    }

    private void DecodeReady()
    {
        while (_recognizer.IsReady(_stream)) _recognizer.Decode(_stream);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _recognizer.Dispose();
    }
}
