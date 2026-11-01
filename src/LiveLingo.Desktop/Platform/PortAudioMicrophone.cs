using System.Runtime.InteropServices;
using PortAudioSharp;

namespace LiveLingo.Desktop.Platform;

/// <summary>The single microphone implementation for macOS and Windows (PortAudio), 40 ms frames at 16 kHz.</summary>
public sealed class PortAudioMicrophone : IMicrophone
{
    public const int SampleRate = 16000;
    public const int FrameSamples = 640;

    private PortAudioSharp.Stream? _stream;
    private PortAudioSharp.Stream.Callback? _callback;

    public bool IsRecording => _stream is not null;

    public string PreferredDevice { get; set; } = "";

    public IReadOnlyList<string> InputDevices()
    {
        PortAudio.Initialize();
        try
        {
            var names = new List<string>();
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0)
                    names.Add(info.name);
            }
            return names;
        }
        finally
        {
            PortAudio.Terminate();
        }
    }

    public string Start(Action<float[]> onFrame)
    {
        if (_stream is not null)
            throw new InvalidOperationException("Already recording.");

        PortAudio.Initialize();
        var device = FindPreferredDevice() ?? PortAudio.DefaultInputDevice;
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

        _stream = new PortAudioSharp.Stream(parameters, null, SampleRate, FrameSamples, StreamFlags.ClipOff, _callback, IntPtr.Zero);
        _stream.Start();
        return info.name;
    }

    public void Stop()
    {
        if (_stream is null)
            return;
        _stream.Stop();
        _stream.Dispose();
        _stream = null;
        _callback = null;
        PortAudio.Terminate();
    }

    /// <summary>The device the user chose, or null for the system default.</summary>
    /// <exception cref="InvalidOperationException">The chosen device is not connected.</exception>
    private int? FindPreferredDevice()
    {
        if (PreferredDevice.Length == 0)
            return null;

        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0 && info.name == PreferredDevice)
                return i;
        }

        PortAudio.Terminate();
        throw new InvalidOperationException($"The input device '{PreferredDevice}' is not available.");
    }

    public void Dispose() => Stop();
}
