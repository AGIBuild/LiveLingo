namespace LiveLingo.Desktop.Platform;

/// <param name="ProcessName">What the user knows the application as ("Slack").</param>
/// <param name="AppId">
/// The application's stable identity, to tell one application from another across runs and renames: the bundle
/// identifier on macOS, the lower-case executable name on Windows. The platform layer is the only place that decides
/// it; empty only when the platform cannot tell (such a target gets no remembered choices).
/// </param>
public record TargetWindowInfo(
    nint Handle,
    nint InputChildHandle,
    string ProcessName,
    string Title,
    int Left, int Top, int Width, int Height,
    string AppId = "");
