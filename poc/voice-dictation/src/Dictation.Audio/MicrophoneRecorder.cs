using System.Runtime.InteropServices;
using Dictation.Core;
using PortAudioSharp;

namespace Dictation.Audio;

/// <summary>
/// Captures the default input device as 16 kHz mono float frames of 40 ms using PortAudio (macOS and Windows).
/// The callback runs on the audio thread: the handler must return quickly and never block.
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    public const int FrameSamples = 640;

    private PortAudioSharp.Stream? _stream;
    private PortAudioSharp.Stream.Callback? _callback;

    public bool IsRecording => _stream is not null;

    public string Start(Action<float[]> onFrame)
    {
        if (_stream is not null) throw new InvalidOperationException("Already recording.");

        PortAudio.Initialize();
        var device = PortAudio.DefaultInputDevice;
        if (device == PortAudio.NoDevice)
        {
            PortAudio.Terminate();
            throw new InvalidOperationException("No default input device.");
        }

        var info = PortAudio.GetDeviceInfo(device);
        var parameters = new StreamParameters
        {
            device = device,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        _callback = (IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData) =>
        {
            var frame = new float[frameCount];
            Marshal.Copy(input, frame, 0, (int)frameCount);
            onFrame(frame);
            return StreamCallbackResult.Continue;
        };

        _stream = new PortAudioSharp.Stream(parameters, null, AudioFormat.SampleRate, FrameSamples, StreamFlags.ClipOff, _callback, IntPtr.Zero);
        _stream.Start();
        return info.name;
    }

    public void Stop()
    {
        if (_stream is null) return;
        _stream.Stop();
        _stream.Dispose();
        _stream = null;
        _callback = null;
        PortAudio.Terminate();
    }

    public void Dispose() => Stop();
}
