#pragma warning disable CA1416

using System.Diagnostics;
using System.Text;
using LiveLingo.Desktop.Platform.macOS;

namespace LiveLingo.Desktop.Tests.Platform;

public class MacClipboardServiceTests
{
    [Fact]
    public async Task SetText_KeepsNonAsciiText_WhenTheProcessHasNoUtf8Locale()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var ct = TestContext.Current.CancellationToken;
        var service = new MacClipboardService();
        var before = await service.GetTextAsync(ct);
        var lang = Environment.GetEnvironmentVariable("LANG");
        var lcAll = Environment.GetEnvironmentVariable("LC_ALL");
        try
        {
            // What an app started from Finder has: no locale at all.
            Environment.SetEnvironmentVariable("LANG", null);
            Environment.SetEnvironmentVariable("LC_ALL", null);

            await service.SetTextAsync("你好，世界 héllo", ct);

            // Read back independently: the service's own pbpaste would undo a wrong encoding in the same way.
            using var paste = Process.Start(new ProcessStartInfo("pbpaste")
            {
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Environment = { ["LANG"] = "en_US.UTF-8" }
            })!;
            Assert.Equal("你好，世界 héllo", await paste.StandardOutput.ReadToEndAsync(ct));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANG", lang);
            Environment.SetEnvironmentVariable("LC_ALL", lcAll);
            await service.SetTextAsync(before ?? "", ct);
        }
    }
}
