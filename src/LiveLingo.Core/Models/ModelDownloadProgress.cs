namespace LiveLingo.Core.Models;

public record ModelDownloadProgress(
    string ModelId,
    long BytesDownloaded,
    long TotalBytes)
{
    public double Percentage => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes * 100
        : 0;
}
