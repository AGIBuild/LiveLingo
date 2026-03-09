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

    private IntPtr _engine;
    private IntPtr _inputNode;
    private readonly MemoryStream _capturedData = new();
    private readonly object _gate = new();
    private bool _isRecording;

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
                0 => MicrophonePermissionState.Unknown,    // NotDetermined
                1 => MicrophonePermissionState.Restricted, // Restricted
                2 => MicrophonePermissionState.Denied,     // Denied
                3 => MicrophonePermissionState.Granted,    // Authorized
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

        var engineClass = MacNativeMethods.objc_getClass("AVAudioEngine");
        var allocSel = MacNativeMethods.sel_registerName("alloc");
        var initSel = MacNativeMethods.sel_registerName("init");

        _engine = MacNativeMethods.objc_msgSend(
            MacNativeMethods.objc_msgSend(engineClass, allocSel), initSel);

        var inputNodeSel = MacNativeMethods.sel_registerName("inputNode");
        _inputNode = MacNativeMethods.objc_msgSend(_engine, inputNodeSel);

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

        var stopSel = MacNativeMethods.sel_registerName("stop");
        MacNativeMethods.objc_msgSend(_engine, stopSel);

        lock (_gate) _isRecording = false;
        CleanupEngine();

        var pcm = _capturedData.ToArray();
        var duration = TimeSpan.FromSeconds(
            (double)pcm.Length / (TargetSampleRate * TargetChannels * 2));
        return Task.FromResult(new AudioCaptureResult(pcm, TargetSampleRate, TargetChannels, duration));
    }

    private void CleanupEngine()
    {
        if (_engine != IntPtr.Zero)
        {
            MacNativeMethods.CFRelease(_engine);
            _engine = IntPtr.Zero;
        }
        _inputNode = IntPtr.Zero;
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
    }
}
