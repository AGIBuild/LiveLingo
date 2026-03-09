namespace LiveLingo.Core.Speech;

/// <summary>
/// Wraps IVoiceActivityDetector to track speech/silence state transitions
/// and fire events when a speech pause is detected.
/// </summary>
public sealed class VoiceActivityMonitor : IDisposable
{
    private readonly IVoiceActivityDetector _detector;
    private readonly float _threshold;
    private readonly float _negThreshold;
    private readonly int _minSilenceFrames;
    private readonly int _minSpeechFrames;

    private bool _speechActive;
    private int _silenceFrameCount;
    private int _speechFrameCount;

    /// <summary>
    /// Fired when the speaker pauses (silence detected after speech).
    /// </summary>
    public event Action? SpeechPauseDetected;

    /// <param name="detector">VAD model</param>
    /// <param name="threshold">Speech probability threshold (default 0.5)</param>
    /// <param name="minSilenceDurationMs">Minimum silence to consider a pause (default 500ms)</param>
    /// <param name="minSpeechDurationMs">Minimum speech before a pause counts (default 250ms)</param>
    public VoiceActivityMonitor(
        IVoiceActivityDetector detector,
        float threshold = 0.5f,
        int minSilenceDurationMs = 500,
        int minSpeechDurationMs = 250)
    {
        _detector = detector;
        _threshold = threshold;
        _negThreshold = threshold - 0.15f;

        var msPerFrame = SileroVadDetector.WindowSize * 1000.0 / SileroVadDetector.SampleRate;
        _minSilenceFrames = (int)Math.Ceiling(minSilenceDurationMs / msPerFrame);
        _minSpeechFrames = (int)Math.Ceiling(minSpeechDurationMs / msPerFrame);
    }

    /// <summary>
    /// Feed PCM float samples. Can be any length; internally splits into 512-sample frames.
    /// </summary>
    public void ProcessSamples(float[] samples, int count)
    {
        var frameBuffer = new float[SileroVadDetector.WindowSize];
        var offset = 0;

        while (offset + SileroVadDetector.WindowSize <= count)
        {
            Array.Copy(samples, offset, frameBuffer, 0, SileroVadDetector.WindowSize);
            var prob = _detector.ProcessFrame(frameBuffer);
            ProcessProbability(prob);
            offset += SileroVadDetector.WindowSize;
        }
    }

    private void ProcessProbability(float prob)
    {
        if (prob >= _threshold)
        {
            _speechFrameCount++;
            _silenceFrameCount = 0;

            if (!_speechActive && _speechFrameCount >= _minSpeechFrames)
                _speechActive = true;
        }
        else if (prob < _negThreshold)
        {
            if (_speechActive)
            {
                _silenceFrameCount++;
                if (_silenceFrameCount >= _minSilenceFrames)
                {
                    SpeechPauseDetected?.Invoke();
                    _speechActive = false;
                    _speechFrameCount = 0;
                    _silenceFrameCount = 0;
                }
            }
            else
            {
                _speechFrameCount = 0;
            }
        }
    }

    public void Reset()
    {
        _detector.Reset();
        _speechActive = false;
        _silenceFrameCount = 0;
        _speechFrameCount = 0;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}
