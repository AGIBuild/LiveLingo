using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace LiveLingo.Desktop.Platform.macOS;

[SupportedOSPlatform("macos")]
internal sealed class MacClipboardService : IClipboardService
{
    // pbcopy and pbpaste read and write bytes in the encoding of the locale. An app started from Finder has no LANG
    // (the C locale: MacRoman), so Chinese came out as mojibake; a terminal-started build has a UTF-8 one.
    private static ProcessStartInfo Tool(string name) => new()
    {
        FileName = name,
        UseShellExecute = false,
        CreateNoWindow = true,
        Environment = { ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8" }
    };

    public async Task SetTextAsync(string text, CancellationToken ct)
    {
        var info = Tool("pbcopy");
        info.RedirectStandardInput = true;
        info.StandardInputEncoding = new UTF8Encoding(false);
        using var proc = new Process { StartInfo = info };
        proc.Start();
        await proc.StandardInput.WriteAsync(text.AsMemory(), ct);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync(ct);
    }

    public async Task<string?> GetTextAsync(CancellationToken ct)
    {
        var info = Tool("pbpaste");
        info.RedirectStandardOutput = true;
        info.StandardOutputEncoding = new UTF8Encoding(false);
        using var proc = new Process { StartInfo = info };
        proc.Start();
        var text = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
