using LiveLingo.Core.Dictation;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>Silero VAD used only to decide where a stream can be cut inside silence.</summary>
public sealed class SherpaSileroVad : IVoiceActivityDetector
{
    private const int WindowSamples = 512;

    /// <summary>Speech probability above which a frame counts as speech. Lower finds quieter and far-field speech.</summary>
    public const float DefaultThreshold = 0.35f;

    private readonly VoiceActivityDetector _vad;
    private readonly Queue<SpeechSpan> _spans = new();
    private readonly int _maxSpeechSamples;
    private readonly float[] _window = new float[WindowSamples];
    private int _windowCount;
    private int _samplesSinceBoundary;
    private bool _continuesSpeech;

    public SherpaSileroVad(string modelPath, float minSilenceSeconds = 0.24f, float threshold = DefaultThreshold, float maxSpeechSeconds = 15f)
    {
        if (!float.IsFinite(maxSpeechSeconds) || maxSpeechSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSpeechSeconds));
        _maxSpeechSamples = checked((int)Math.Ceiling((double)maxSpeechSeconds * AudioFormat.SampleRate));
        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = threshold;
        config.SileroVad.MinSilenceDuration = minSilenceSeconds;
        config.SileroVad.MinSpeechDuration = 0.2f;
        config.SileroVad.WindowSize = WindowSamples;
        config.SileroVad.MaxSpeechDuration = maxSpeechSeconds;
        config.SampleRate = AudioFormat.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        _vad = new VoiceActivityDetector(config, Math.Max(120f, maxSpeechSeconds + 1f));
    }

    public void Accept(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var count = Math.Min(samples.Length, WindowSamples - _windowCount);
            samples[..count].CopyTo(_window.AsSpan(_windowCount));
            _windowCount += count;
            samples = samples[count..];
            if (_windowCount < WindowSamples)
                continue;

            _vad.AcceptWaveform(_window);
            _windowCount = 0;
            _continuesSpeech = false;
            _samplesSinceBoundary += WindowSamples;
            Drain();
            if (_samplesSinceBoundary >= _maxSpeechSamples)
            {
                // sherpa's MaxSpeechDuration only changes thresholds. Flush bounds its retained audio without
                // resetting the model or the absolute sample clock; this boundary is not a silence pause.
                _continuesSpeech = _vad.IsSpeechDetected();
                _vad.Flush();
                Drain();
                _samplesSinceBoundary = 0;
            }
        }
    }

    public bool IsSpeechActive => _continuesSpeech || _vad.IsSpeechDetected();

    public bool TryTake(out SpeechSpan span) => _spans.TryDequeue(out span);

    private void Drain()
    {
        while (!_vad.IsEmpty())
        {
            var segment = _vad.Front();
            _spans.Enqueue(new SpeechSpan(segment.Start, segment.Start + segment.Samples.Length));
            _vad.Pop();
            _samplesSinceBoundary = 0;
        }
    }

    public void Flush()
    {
        if (_windowCount > 0)
        {
            // Silero VAD's native circular buffer requires WindowSamples-aligned (512-sample) frames; feeding a
            // partial tail causes sherpa-onnx Flush() to pop a negative count (e.g. 256 - 512 = -256).
            _window.AsSpan(_windowCount).Clear();
            _vad.AcceptWaveform(_window);
            _windowCount = 0;
        }
        _vad.Flush();
        Drain();
        _continuesSpeech = false;
        _samplesSinceBoundary = 0;
    }

    public void Dispose() => _vad.Dispose();
}
