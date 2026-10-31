namespace LiveLingo.Core.Tests.Support;

/// <summary>A timer that only fires when a test calls <see cref="Fire"/>.</summary>
public sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
{
    public TimeSpan Due { get; private set; } = Timeout.InfiniteTimeSpan;
    public bool Disposed { get; private set; }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        Due = dueTime;
        return true;
    }

    public void Fire() => callback(state);

    public void Dispose() => Disposed = true;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed class ManualTimeProvider : TimeProvider
{
    public List<ManualTimer> Timers { get; } = [];

    public ManualTimer Timer => Timers[^1];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        Timers.Add(timer);
        return timer;
    }
}
