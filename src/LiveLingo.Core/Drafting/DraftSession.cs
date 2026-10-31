namespace LiveLingo.Core.Drafting;

/// <summary>
/// Serial owner of a <see cref="Draft"/>: every read and change goes through one lock, and observers are told
/// after each change on <c>notifyOn</c> (the UI thread's context in the app) or inline when it is null.
/// </summary>
public sealed class DraftSession
{
    private readonly object _gate = new();
    private readonly Draft _draft;
    private readonly SynchronizationContext? _context;

    public DraftSession(bool hasTarget, SynchronizationContext? notifyOn = null)
    {
        _draft = new Draft(hasTarget);
        _context = notifyOn;
    }

    public event Action? Changed;

    public T Read<T>(Func<Draft, T> read)
    {
        lock (_gate)
            return read(_draft);
    }

    public T Mutate<T>(Func<Draft, T> change)
    {
        T result;
        lock (_gate)
            result = change(_draft);
        NotifyChanged();
        return result;
    }

    public void Mutate(Action<Draft> change) =>
        Mutate(draft =>
        {
            change(draft);
            return 0;
        });

    private void NotifyChanged()
    {
        if (_context is null)
            Changed?.Invoke();
        else
            _context.Post(_ => Changed?.Invoke(), null);
    }
}
