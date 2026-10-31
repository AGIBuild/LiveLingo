using LiveLingo.Core.Dictation;
using SherpaOnnx;

namespace LiveLingo.Core.Speech.Native;

/// <summary>Silero VAD used only to decide where a stream can be cut inside silence.</summary>
public sealed class SherpaSileroVad : IVoiceActivityDetector
{
    /// <summary>Speech probability above which a frame counts as speech. Lower finds quieter and far-field speech.</summary>
    public const float DefaultThreshold = 0.35f;

    private readonly VoiceActivityDetector _vad;

    public SherpaSileroVad(string modelPath, float minSilenceSeconds = 0.3f, float threshold = DefaultThreshold, float maxSpeechSeconds = 20f)
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = threshold;
        config.SileroVad.MinSilenceDuration = minSilenceSeconds;
        config.SileroVad.MinSpeechDuration = 0.2f;
        config.SileroVad.WindowSize = 512;
        config.SileroVad.MaxSpeechDuration = maxSpeechSeconds;
        config.SampleRate = AudioFormat.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        _vad = new VoiceActivityDetector(config, 120f);
    }

    public void Accept(ReadOnlySpan<float> samples) => _vad.AcceptWaveform(samples.ToArray());

    public bool IsSpeechActive => _vad.IsSpeechDetected();

    public bool TryTake(out SpeechSpan span)
    {
        if (_vad.IsEmpty())
        {
            span = default;
            return false;
        }

        var segment = _vad.Front();
        span = new SpeechSpan(segment.Start, segment.Start + segment.Samples.Length);
        _vad.Pop();
        return true;
    }

    public void Flush() => _vad.Flush();

    public void Dispose() => _vad.Dispose();
}
