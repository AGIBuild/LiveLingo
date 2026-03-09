namespace LiveLingo.Core.Speech;

public record AudioCaptureResult(
    byte[] PcmData,
    int SampleRate,
    int Channels,
    TimeSpan Duration);
