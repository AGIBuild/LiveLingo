namespace LiveLingo.Desktop.Services.Update;

/// <summary>
/// The installed application's update feed. An update is downloaded as soon as it is found and then waits, ready to
/// be installed, until the user restarts or quits the application.
/// </summary>
public interface IUpdateService
{
    /// <summary>The version that was downloaded and is ready to install; null until there is one.</summary>
    string? ReadyVersion { get; }

    /// <summary>Looks for a newer version and downloads it. True when a version is ready to install.</summary>
    Task<bool> CheckAndDownloadAsync(CancellationToken ct = default);

    /// <summary>Installs <see cref="ReadyVersion"/> now and starts the new version.</summary>
    void ApplyAndRestart();

    /// <summary>Installs <see cref="ReadyVersion"/> silently once this process has exited.</summary>
    void ApplyOnExit();
}
