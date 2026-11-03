namespace LiveLingo.Desktop.Services.Update;

/// <summary>
/// The installed application's update feed. An update is downloaded as soon as it is found and then waits, ready to
/// be installed, until the user restarts or quits the application.
/// </summary>
public interface IUpdateService
{
    /// <summary>False when this copy was not put in place by the installer (a development build, the MSI): it cannot update itself.</summary>
    bool CanUpdate { get; }

    /// <summary>Where the installers of the latest version can be downloaded by hand; null when the feed is not a web page.</summary>
    string? DownloadPage { get; }

    /// <summary>The version that was downloaded and is ready to install; null until there is one.</summary>
    string? ReadyVersion { get; }

    /// <summary>Asks the feed for a newer version. Null when this one is the latest.</summary>
    Task<string?> FindAsync(CancellationToken ct = default);

    /// <summary>Downloads the version <see cref="FindAsync"/> found, reporting 0-100 as it goes.</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken ct = default);

    /// <summary>Installs <see cref="ReadyVersion"/> now and starts the new version.</summary>
    void ApplyAndRestart();

    /// <summary>Installs <see cref="ReadyVersion"/> silently once this process has exited.</summary>
    void ApplyOnExit();
}
