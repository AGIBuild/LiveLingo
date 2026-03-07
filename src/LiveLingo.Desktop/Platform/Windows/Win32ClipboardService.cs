using System.Runtime.InteropServices;
using static LiveLingo.Desktop.Platform.Windows.NativeMethods;

namespace LiveLingo.Desktop.Platform.Windows;

internal sealed class Win32ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken ct)
    {
        return Task.Run(() => SetClipboardText(text), ct);
    }

    public Task<string?> GetTextAsync(CancellationToken ct)
    {
        return Task.FromResult<string?>(null);
    }

    internal static void SetClipboardText(string text)
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
                    if (hGlobal == IntPtr.Zero) return;

                    var ptr = GlobalLock(hGlobal);
                    if (ptr == IntPtr.Zero) return;

                    Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
                    Marshal.WriteInt16(ptr, text.Length * 2, 0);
                    GlobalUnlock(hGlobal);
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                    return;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(30);
        }
    }
}
