namespace LiveLingo.App.Platform;

public interface ITextInjector
{
    Task InjectAsync(
        TargetWindowInfo target,
        string text,
        bool autoSend,
        CancellationToken ct = default);
}
