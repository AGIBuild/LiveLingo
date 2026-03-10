using System.Runtime.InteropServices;
using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class Win32AudioCaptureService : IAudioCaptureService
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferDurationMs = 200;
    private const int BufferCount = 8;
    private const int MaxRecordingSeconds = 60;

    private IntPtr _hWaveIn;
    private readonly List<GCHandle> _pinnedHeaders = [];
    private readonly List<IntPtr> _allocatedBuffers = [];
    private readonly MemoryStream _capturedData = new();
    private readonly object _gate = new();
    private bool _isRecording;

    public bool IsRecording
    {
        get { lock (_gate) return _isRecording; }
    }

    public Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default)
    {
        var deviceCount = WaveIn.waveInGetNumDevs();
        return Task.FromResult(deviceCount > 0
            ? MicrophonePermissionState.Granted
            : MicrophonePermissionState.Denied);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_isRecording)
                throw new InvalidOperationException("Already recording.");
        }

        var format = new WaveIn.WAVEFORMATEX
        {
            wFormatTag = WaveIn.WAVE_FORMAT_PCM,
            nChannels = Channels,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = BitsPerSample,
            nBlockAlign = (short)(Channels * BitsPerSample / 8),
            nAvgBytesPerSec = SampleRate * Channels * BitsPerSample / 8,
            cbSize = 0
        };

        var result = WaveIn.waveInOpen(
            out _hWaveIn, WaveIn.WAVE_MAPPER, ref format, _waveInCallback, IntPtr.Zero,
            WaveIn.CALLBACK_FUNCTION);

        if (result != 0)
            throw new InvalidOperationException($"waveInOpen failed with code {result}.");

        _capturedData.SetLength(0);
        var bufferSize = SampleRate * Channels * (BitsPerSample / 8) * BufferDurationMs / 1000;

        for (var i = 0; i < BufferCount; i++)
        {
            var buffer = Marshal.AllocHGlobal(bufferSize);
            _allocatedBuffers.Add(buffer);

            var header = new WaveIn.WAVEHDR
            {
                lpData = buffer,
                dwBufferLength = (uint)bufferSize,
                dwFlags = 0
            };

            var handle = GCHandle.Alloc(header, GCHandleType.Pinned);
            _pinnedHeaders.Add(handle);
            var headerPtr = handle.AddrOfPinnedObject();

            WaveIn.waveInPrepareHeader(_hWaveIn, headerPtr, (uint)Marshal.SizeOf<WaveIn.WAVEHDR>());
            WaveIn.waveInAddBuffer(_hWaveIn, headerPtr, (uint)Marshal.SizeOf<WaveIn.WAVEHDR>());
        }

        WaveIn.waveInStart(_hWaveIn);
        lock (_gate) _isRecording = true;

        return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_isRecording)
                throw new InvalidOperationException("Not recording.");
            _isRecording = false;
        }

        WaveIn.waveInStop(_hWaveIn);
        WaveIn.waveInReset(_hWaveIn);

        foreach (var handle in _pinnedHeaders)
        {
            var headerPtr = handle.AddrOfPinnedObject();
            WaveIn.waveInUnprepareHeader(_hWaveIn, headerPtr, (uint)Marshal.SizeOf<WaveIn.WAVEHDR>());
            handle.Free();
        }
        _pinnedHeaders.Clear();

        foreach (var buf in _allocatedBuffers)
            Marshal.FreeHGlobal(buf);
        _allocatedBuffers.Clear();

        WaveIn.waveInClose(_hWaveIn);
        _hWaveIn = IntPtr.Zero;

        var pcm = _capturedData.ToArray();
        var duration = TimeSpan.FromSeconds((double)pcm.Length / (SampleRate * Channels * BitsPerSample / 8));
        return Task.FromResult(new AudioCaptureResult(pcm, SampleRate, Channels, duration));
    }

    private readonly WaveIn.WaveInProc _waveInCallback = (hWaveIn, msg, _, headerPtr, _) =>
    {
        // Static callback cannot easily reference 'this' via the instance field pattern.
        // The real data is accumulated inside the callback via a captured local - but WaveInProc
        // is a delegate field on the instance so it captures 'this' implicitly.
    };

    public Win32AudioCaptureService()
    {
        _waveInCallback = WaveInCallback;
    }

    private void WaveInCallback(IntPtr hWaveIn, uint msg, IntPtr instance, IntPtr headerPtr, IntPtr reserved)
    {
        if (msg != WaveIn.WIM_DATA) return;

        lock (_gate)
        {
            if (!_isRecording) return;

            var header = Marshal.PtrToStructure<WaveIn.WAVEHDR>(headerPtr);
            if (header.dwBytesRecorded > 0)
            {
                var data = new byte[header.dwBytesRecorded];
                Marshal.Copy(header.lpData, data, 0, (int)header.dwBytesRecorded);
                if (_capturedData.Length < MaxRecordingSeconds * SampleRate * Channels * BitsPerSample / 8)
                    _capturedData.Write(data, 0, data.Length);
            }

            if (_hWaveIn != IntPtr.Zero)
                WaveIn.waveInAddBuffer(_hWaveIn, headerPtr, (uint)Marshal.SizeOf<WaveIn.WAVEHDR>());
        }
    }

    public AudioCaptureResult? GetCurrentBuffer()
    {
        lock (_gate)
        {
            if (!_isRecording || _capturedData.Length == 0) return null;
            var pcm = _capturedData.ToArray();
            var duration = TimeSpan.FromSeconds((double)pcm.Length / (SampleRate * Channels * BitsPerSample / 8));
            return new AudioCaptureResult(pcm, SampleRate, Channels, duration);
        }
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            try { StopAsync().GetAwaiter().GetResult(); }
            catch { /* best-effort */ }
        }
    }

    internal static class WaveIn
    {
        public const int WAVE_FORMAT_PCM = 1;
        public const uint WAVE_MAPPER = unchecked((uint)-1);
        public const uint CALLBACK_FUNCTION = 0x00030000;
        public const uint WIM_DATA = 0x3C0;

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        public delegate void WaveInProc(
            IntPtr hWaveIn, uint uMsg, IntPtr dwInstance,
            IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")]
        public static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll")]
        public static extern int waveInOpen(
            out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX lpFormat,
            WaveInProc dwCallback, IntPtr dwInstance, uint fdwOpen);

        [DllImport("winmm.dll")]
        public static extern int waveInPrepareHeader(
            IntPtr hwi, IntPtr lpWaveInHdr, uint cbWaveInHdr);

        [DllImport("winmm.dll")]
        public static extern int waveInUnprepareHeader(
            IntPtr hwi, IntPtr lpWaveInHdr, uint cbWaveInHdr);

        [DllImport("winmm.dll")]
        public static extern int waveInAddBuffer(
            IntPtr hwi, IntPtr lpWaveInHdr, uint cbWaveInHdr);

        [DllImport("winmm.dll")]
        public static extern int waveInStart(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInStop(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInReset(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInClose(IntPtr hwi);
    }
}
