using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static LiveLingo.Desktop.Platform.macOS.MacNativeMethods;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacMicrophonePermission : IMicrophonePermission
{
    private const string AvFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
    private const long AuthorizationNotDetermined = 0;
    private const long AuthorizationAuthorized = 3;

    public MicrophoneAccess Status
    {
        get
        {
            NativeLibrary.Load(AvFoundation);
            var mediaType = CreateCFString("soun"); // AVMediaTypeAudio
            try
            {
                var status = objc_msgSend_long(
                    objc_getClass("AVCaptureDevice"),
                    sel_registerName("authorizationStatusForMediaType:"),
                    mediaType);
                return status switch
                {
                    AuthorizationNotDetermined => MicrophoneAccess.NotDetermined,
                    AuthorizationAuthorized => MicrophoneAccess.Granted,
                    _ => MicrophoneAccess.Denied
                };
            }
            finally
            {
                CFRelease(mediaType);
            }
        }
    }

    public void OpenSettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "open",
            Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone",
            UseShellExecute = false
        });
}
