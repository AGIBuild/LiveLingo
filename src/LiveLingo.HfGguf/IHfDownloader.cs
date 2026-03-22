namespace LiveLingo.HfGguf;

public interface IHfDownloader
{
    Task DownloadAsync(
        string repoId,
        string revision,
        string filePath,
        string destinationFilePath,
        string? bearerToken,
        bool forceRestart,
        int bufferSize,
        IProgress<HfDownloadProgress>? progress,
        CancellationToken cancellationToken = default,
        string? hubResolveBaseOverride = null);
}
