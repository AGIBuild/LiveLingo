namespace LiveLingo.Desktop.Platform;

public record TargetWindowInfo(
    nint Handle,
    nint InputChildHandle,
    string ProcessName,
    string Title,
    int Left, int Top, int Width, int Height);
