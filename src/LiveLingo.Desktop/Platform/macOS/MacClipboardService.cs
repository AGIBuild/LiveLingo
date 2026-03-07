using System.Diagnostics;
using System.Runtime.Versioning;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text, CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        await proc.StandardInput.WriteAsync(text.AsMemory(), ct);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);
    }

    public async Task<string?> GetTextAsync(CancellationToken ct)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pbpaste",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        var text = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
