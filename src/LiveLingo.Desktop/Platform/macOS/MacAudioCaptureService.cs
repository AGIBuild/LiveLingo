using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacAudioCaptureService : IAudioCaptureService
{
    private const int TargetSampleRate = 16000;
    private const int TargetChannels = 1;
    private const int MaxRecordingSeconds = 60;
    private const int MaxCaptureBytes = MaxRecordingSeconds * TargetSampleRate * TargetChannels * 2;
    private const uint TapBufferFrames = 4096;

    private IntPtr _engine;
    private IntPtr _inputNode;
    private readonly MemoryStream _capturedData = new();
    private readonly object _gate = new();
    private bool _isRecording;

    private IntPtr _blockPtr;
    private IntPtr _descriptorPtr;
    private GCHandle _delegateHandle;
    private static MacAudioCaptureService? _activeInstance;

    public bool IsRecording
    {
        get { lock (_gate) return _isRecording; }
    }

    public Task<MicrophonePermissionState> GetPermissionStateAsync(CancellationToken ct = default)
    {
        var avClass = MacNativeMethods.objc_getClass("AVCaptureDevice");
        var mediaSel = MacNativeMethods.sel_registerName("authorizationStatusForMediaType:");
        var audioType = CreateNSString("soun");

        try
        {
            var status = MacAudioNative.objc_msgSend_int_ptr(avClass, mediaSel, audioType);
            return Task.FromResult(status switch
            {
                0 => MicrophonePermissionState.Unknown,
                1 => MicrophonePermissionState.Restricted,
                2 => MicrophonePermissionState.Denied,
                3 => MicrophonePermissionState.Granted,
                _ => MicrophonePermissionState.Unknown
            });
        }
        finally
        {
            MacNativeMethods.CFRelease(audioType);
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_isRecording)
                throw new InvalidOperationException("Already recording.");
        }

        _capturedData.SetLength(0);
        _activeInstance = this;

        var engineClass = MacNativeMethods.objc_getClass("AVAudioEngine");
        var allocSel = MacNativeMethods.sel_registerName("alloc");
        var initSel = MacNativeMethods.sel_registerName("init");

        _engine = MacNativeMethods.objc_msgSend(
            MacNativeMethods.objc_msgSend(engineClass, allocSel), initSel);

        var inputNodeSel = MacNativeMethods.sel_registerName("inputNode");
        _inputNode = MacNativeMethods.objc_msgSend(_engine, inputNodeSel);

        InstallTapOnInputNode();

        var startSel = MacNativeMethods.sel_registerName("startAndReturnError:");
        IntPtr error = IntPtr.Zero;
        var started = MacAudioNative.objc_msgSend_bool_ref(
            _engine, startSel, ref error);

        if (!started)
        {
            CleanupEngine();
            throw new InvalidOperationException("AVAudioEngine failed to start.");
        }

        lock (_gate) _isRecording = true;
        return Task.CompletedTask;
    }

    public Task<AudioCaptureResult> StopAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_isRecording)
                throw new InvalidOperationException("Not recording.");
        }

        var removeTapSel = MacNativeMethods.sel_registerName("removeTapOnBus:");
        MacAudioNative.objc_msgSend_nuint(_inputNode, removeTapSel, 0);

        var stopSel = MacNativeMethods.sel_registerName("stop");
        MacNativeMethods.objc_msgSend(_engine, stopSel);

        lock (_gate) _isRecording = false;
        _activeInstance = null;
        CleanupEngine();

        var pcm = _capturedData.ToArray();
        var duration = TimeSpan.FromSeconds(
            (double)pcm.Length / (TargetSampleRate * TargetChannels * 2));
        return Task.FromResult(new AudioCaptureResult(pcm, TargetSampleRate, TargetChannels, duration));
    }

    public AudioCaptureResult? GetCurrentBuffer()
    {
        lock (_gate)
        {
            if (!_isRecording || _capturedData.Length == 0) return null;
            var pcm = _capturedData.ToArray();
            var duration = TimeSpan.FromSeconds(
                (double)pcm.Length / (TargetSampleRate * TargetChannels * 2));
            return new AudioCaptureResult(pcm, TargetSampleRate, TargetChannels, duration);
        }
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            try { StopAsync().GetAwaiter().GetResult(); }
            catch { /* best-effort */ }
        }
        CleanupEngine();
    }

    #region AVAudioEngine tap

    private void InstallTapOnInputNode()
    {
        _blockPtr = CreateTapBlock();

        var tapSel = MacNativeMethods.sel_registerName(
            "installTapOnBus:bufferSize:format:block:");
        MacAudioNative.objc_msgSend_installTap(
            _inputNode, tapSel,
            0,
            TapBufferFrames,
            IntPtr.Zero, // nil = use the node's native hardware format
            _blockPtr);
    }

    private IntPtr CreateTapBlock()
    {
        var tapDelegate = new TapBlockInvoke(TapCallbackStatic);
        _delegateHandle = GCHandle.Alloc(tapDelegate);

        var objcLib = NativeLibrary.Load("/usr/lib/libobjc.dylib");
        var globalBlockIsa = NativeLibrary.GetExport(objcLib, "_NSConcreteGlobalBlock");

        _descriptorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(new BlockDescriptor
        {
            reserved = 0,
            size = (ulong)Marshal.SizeOf<BlockLiteral>()
        }, _descriptorPtr, false);

        var blockPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(new BlockLiteral
        {
            isa = globalBlockIsa,
            flags = 1 << 28,
            reserved = 0,
            invoke = Marshal.GetFunctionPointerForDelegate(tapDelegate),
            descriptor = _descriptorPtr
        }, blockPtr, false);

        return blockPtr;
    }

    private static void TapCallbackStatic(IntPtr block, IntPtr pcmBuffer, IntPtr when)
    {
        var instance = _activeInstance;
        if (instance is null) return;

        try
        {
            var frameLengthSel = MacNativeMethods.sel_registerName("frameLength");
            var frameLength = MacAudioNative.objc_msgSend_uint(pcmBuffer, frameLengthSel);
            if (frameLength == 0) return;

            var formatSel = MacNativeMethods.sel_registerName("format");
            var bufferFormat = MacNativeMethods.objc_msgSend(pcmBuffer, formatSel);

            var sampleRateSel = MacNativeMethods.sel_registerName("sampleRate");
            var sourceSampleRate = (int)Math.Round(
                MacAudioNative.objc_msgSend_double(bufferFormat, sampleRateSel));

            var channelCountSel = MacNativeMethods.sel_registerName("channelCount");
            var channelCount = MacAudioNative.objc_msgSend_uint(bufferFormat, channelCountSel);

            var floatDataSel = MacNativeMethods.sel_registerName("floatChannelData");
            var floatDataPtr = MacNativeMethods.objc_msgSend(pcmBuffer, floatDataSel);
            if (floatDataPtr == IntPtr.Zero) return;

            var channel0Ptr = Marshal.ReadIntPtr(floatDataPtr);
            if (channel0Ptr == IntPtr.Zero) return;
            var channel0 = new float[frameLength];
            Marshal.Copy(channel0Ptr, channel0, 0, (int)frameLength);

            float[] mono;
            if (channelCount >= 2)
            {
                var channel1Ptr = Marshal.ReadIntPtr(floatDataPtr, IntPtr.Size);
                var channel1 = new float[frameLength];
                if (channel1Ptr != IntPtr.Zero)
                    Marshal.Copy(channel1Ptr, channel1, 0, (int)frameLength);
                mono = new float[frameLength];
                for (var i = 0; i < (int)frameLength; i++)
                    mono[i] = (channel0[i] + channel1[i]) * 0.5f;
            }
            else
            {
                mono = channel0;
            }

            var resampled = ResampleLinear(mono, sourceSampleRate, TargetSampleRate);

            var pcmBytes = new byte[resampled.Length * 2];
            for (var i = 0; i < resampled.Length; i++)
            {
                var clamped = Math.Clamp(resampled[i], -1f, 1f);
                var int16 = (short)(clamped * 32767f);
                pcmBytes[i * 2] = (byte)(int16 & 0xFF);
                pcmBytes[i * 2 + 1] = (byte)((int16 >> 8) & 0xFF);
            }

            lock (instance._gate)
            {
                if (instance._isRecording && instance._capturedData.Length < MaxCaptureBytes)
                    instance._capturedData.Write(pcmBytes, 0, pcmBytes.Length);
            }
        }
        catch
        {
            // Swallow exceptions in native callback to prevent process crash
        }
    }

    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return input;

        var ratio = (double)sourceRate / targetRate;
        var outputLength = (int)(input.Length / ratio);
        if (outputLength <= 0) return [];

        var output = new float[outputLength];
        for (var i = 0; i < outputLength; i++)
        {
            var srcPos = i * ratio;
            var srcIdx = (int)srcPos;
            var frac = (float)(srcPos - srcIdx);

            if (srcIdx + 1 < input.Length)
                output[i] = input[srcIdx] * (1f - frac) + input[srcIdx + 1] * frac;
            else if (srcIdx < input.Length)
                output[i] = input[srcIdx];
        }

        return output;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TapBlockInvoke(IntPtr block, IntPtr buffer, IntPtr when);

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr isa;
        public int flags;
        public int reserved;
        public IntPtr invoke;
        public IntPtr descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public ulong reserved;
        public ulong size;
    }

    #endregion

    private void CleanupEngine()
    {
        if (_engine != IntPtr.Zero)
        {
            MacNativeMethods.CFRelease(_engine);
            _engine = IntPtr.Zero;
        }
        _inputNode = IntPtr.Zero;

        if (_blockPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_blockPtr);
            _blockPtr = IntPtr.Zero;
        }
        if (_descriptorPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_descriptorPtr);
            _descriptorPtr = IntPtr.Zero;
        }
        if (_delegateHandle.IsAllocated)
            _delegateHandle.Free();
    }

    private static IntPtr CreateNSString(string str)
    {
        var nsStringClass = MacNativeMethods.objc_getClass("NSString");
        var sel = MacNativeMethods.sel_registerName("stringWithUTF8String:");
        return MacAudioNative.objc_msgSend_ptr_utf8(nsStringClass, sel, str);
    }

    internal static class MacAudioNative
    {
        private const string ObjC = "/usr/lib/libobjc.dylib";

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern int objc_msgSend_int_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool objc_msgSend_bool_ref(
            IntPtr receiver, IntPtr selector, ref IntPtr error);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern IntPtr objc_msgSend_ptr_utf8(
            IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern void objc_msgSend_installTap(
            IntPtr receiver, IntPtr selector,
            nuint bus,
            uint bufferSize,
            IntPtr format,
            IntPtr block);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern void objc_msgSend_nuint(
            IntPtr receiver, IntPtr selector,
            nuint arg);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern uint objc_msgSend_uint(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);
    }
}
