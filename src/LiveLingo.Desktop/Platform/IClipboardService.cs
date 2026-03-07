namespace LiveLingo.Desktop.Platform;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken ct = default);
    Task<string?> GetTextAsync(CancellationToken ct = default);
}
