namespace LiveLingo.Core.Speech;

public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>
    /// Process a single audio frame (512 samples at 16kHz) and return speech probability [0, 1].
    /// </summary>
    float ProcessFrame(float[] samples);

    void Reset();
}
