namespace LiveLingo.Desktop.Platform;

public interface IWindowTracker
{
    TargetWindowInfo? GetForegroundWindowInfo();

    /// <summary>True while the window captured earlier still exists, so text is never injected into a different window.</summary>
    bool IsAlive(TargetWindowInfo target);
}
