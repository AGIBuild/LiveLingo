namespace LiveLingo.HfGguf;

/// <param name="DownloadedBytes">Bytes written for this download attempt (including resumed offset).</param>
/// <param name="TotalBytes">Total size when known (full file), otherwise null.</param>
public readonly record struct HfDownloadProgress(long DownloadedBytes, long? TotalBytes);
