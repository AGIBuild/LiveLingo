namespace LiveLingo.Core.Models;

/// <summary>
/// Default in-process implementation of <see cref="ITranslationTelemetry"/>. Keeps a
/// bounded ring buffer of recent traces and broadcasts each recorded trace on a
/// synchronous event. Designed to be lock-efficient on the hot translation path
/// (recording a trace is O(1) and holds the lock only long enough to enqueue).
/// </summary>
public sealed class InProcessTranslationTelemetry : ITranslationTelemetry
{
    public const int DefaultCapacity = 100;

    private readonly int _capacity;
    private readonly Queue<TranslationRouteTrace> _buffer;
    private readonly Lock _gate = new();

    public InProcessTranslationTelemetry() : this(DefaultCapacity) { }

    public InProcessTranslationTelemetry(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _buffer = new Queue<TranslationRouteTrace>(capacity);
    }

    public event Action<TranslationRouteTrace>? RouteCompleted;

    public IReadOnlyList<TranslationRouteTrace> RecentTraces
    {
        get
        {
            lock (_gate)
            {
                return [.. _buffer];
            }
        }
    }

    public void Record(TranslationRouteTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        lock (_gate)
        {
            if (_buffer.Count >= _capacity)
                _buffer.Dequeue();
            _buffer.Enqueue(trace);
        }
        // Invoke listeners outside the lock: they may run arbitrary code (UI
        // marshalling, disk IO) and we must not stall concurrent Record() calls.
        RouteCompleted?.Invoke(trace);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }
}
