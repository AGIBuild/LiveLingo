namespace LiveLingo.Desktop.Composer;

/// <summary>
/// Watches microphone frames for a dead input (muted, wrong device, no permission delivers silence).
/// It reports when the input has been silent for a while and again when sound returns.
/// </summary>
public sealed class SignalMonitor(TimeSpan silenceTimeout)
{
    /// <summary>A frame whose peak is below this is silence; speech peaks are far higher.</summary>
    public const float SpeechPeak = 0.01f;

    private const int SampleRate = 16_000;

    private long _quietSamples;
    private bool _reported;

    /// <summary>Returns true when silence was just reported, false when sound just returned, null otherwise.</summary>
    public bool? Observe(ReadOnlySpan<float> frame)
    {
        var peak = 0f;
        foreach (var sample in frame)
            peak = Math.Max(peak, Math.Abs(sample));

        if (peak >= SpeechPeak)
        {
            _quietSamples = 0;
            if (!_reported)
                return null;

            _reported = false;
            return false;
        }

        _quietSamples += frame.Length;
        if (_reported || _quietSamples < silenceTimeout.TotalSeconds * SampleRate)
            return null;

        _reported = true;
        return true;
    }
}
