using System.Diagnostics;

namespace LiveLingo.Desktop.Platform;

internal static class BrowserLauncher
{
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url.Trim(),
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening the default browser can fail in restricted environments; no user-recoverable action here.
        }
    }
}
