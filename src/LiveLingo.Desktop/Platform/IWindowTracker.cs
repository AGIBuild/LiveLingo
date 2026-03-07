namespace LiveLingo.Desktop.Platform;

public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();
}
