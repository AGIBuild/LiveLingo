namespace LiveLingo.App.Platform;

public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();
}
