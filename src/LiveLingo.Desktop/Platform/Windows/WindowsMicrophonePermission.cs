using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LiveLingo.Desktop.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsMicrophonePermission : IMicrophonePermission
{
    private const string ConsentKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    // Desktop apps have no per-app prompt: capture is allowed unless the user turned the global switch off.
    public MicrophoneAccess Status =>
        Registry.CurrentUser.OpenSubKey(ConsentKey)?.GetValue("Value") is "Deny"
            ? MicrophoneAccess.Denied
            : MicrophoneAccess.Granted;

    public void OpenSettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone")
        {
            UseShellExecute = true
        });
}
