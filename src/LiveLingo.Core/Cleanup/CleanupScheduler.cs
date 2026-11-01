using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// Cleans the sentences of a <see cref="DraftSession"/> while the user is still speaking: one request at a time,
/// always for the oldest sentence that has none. A sentence is cleaned once something follows it, because a long
/// thinking pause can split one sentence in two; the last sentence waits at most <paramref name="holdback"/>.
/// A result that fails the <see cref="FidelityGate"/> keeps the original sentence and records the reason.
/// </summary>
public sealed class CleanupScheduler : IAsyncDisposable
{
    private readonly DraftSession _session;
    private readonly ITextCleaner _cleaner;
    private readonly IReadOnlyList<string> _glossary;
    private readonly TimeSpan _holdback;
    private readonly int _contextUnits;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ITimer _holdbackTimer;
    private readonly Dictionary<int, DateTimeOffset> _firstSeen = [];

    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;
    private (JobTicket Ticket, CancellationTokenSource Cancel)? _inFlight;

    public CleanupScheduler(
        DraftSession session,
        ITextCleaner cleaner,
        IReadOnlyList<string> glossary,
        TimeSpan holdback,
        int contextUnits,
        TimeProvider? time = null)
    {
        _session = session;
        _cleaner = cleaner;
        _glossary = glossary;
        _holdback = holdback;
        _contextUnits = contextUnits;
        _time = time ?? TimeProvider.System;
        _holdbackTimer = _time.CreateTimer(_ => Wake(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _session.Changed += OnChanged;
        OnChanged();
    }

    private void Wake()
    {
        lock (_gate)
        {
            if (!_disposed)
                StartWorkerIfNeeded();
        }
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var now = _time.GetUtcNow();
            foreach (var id in _session.Read(draft => draft.Units.Select(unit => unit.Id).ToList()))
                _firstSeen.TryAdd(id, now);

            if (_inFlight is { } flight && !_session.Read(draft => draft.IsCurrent(flight.Ticket, UnitKind.Clean)))
                flight.Cancel.Cancel();

            StartWorkerIfNeeded();
        }
    }

    private void StartWorkerIfNeeded()
    {
        if (_running)
            return;

        _running = true;
        _worker = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (true)
        {
            JobTicket ticket;
            CleanupRequest request;
            CancellationTokenSource cancel;
            lock (_gate)
            {
                var (next, wait) = _disposed ? (null, null) : _session.Read(draft => Find(draft, _time.GetUtcNow()));
                if (next is null)
                {
                    // Once disposed the timer must not be touched: changing a disposed timer throws.
                    if (!_disposed)
                        _holdbackTimer.Change(wait ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _running = false;
                    return;
                }

                (ticket, request) = next.Value;
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _inFlight = (ticket, cancel);
            }

            try
            {
                var cleaned = (await _cleaner.CleanAsync(request, cancel.Token).ConfigureAwait(false)).Trim();
                var gate = FidelityGate.Check(request.Target, cleaned, _glossary);
                _session.Mutate(draft => draft.Complete(ticket, UnitKind.Clean, cleaned, gate.Rejection));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // The sentence changed or the scheduler is stopping; the next scan decides what to do.
            }
            catch (Exception ex) when (ServiceFailure.IsUnavailable(ex))
            {
                _session.Mutate(draft => draft.FailPending(ticket, UnitKind.Clean, ex.Message));
            }
            catch (Exception ex)
            {
                _session.Mutate(draft => draft.Fail(ticket, UnitKind.Clean, ex.Message));
            }
            finally
            {
                lock (_gate)
                    _inFlight = null;
                cancel.Dispose();
            }
        }
    }

    /// <summary>The next sentence to clean, or how long the oldest waiting sentence is still held back.</summary>
    private ((JobTicket Ticket, CleanupRequest Request)? Next, TimeSpan? Wait) Find(Draft draft, DateTimeOffset now)
    {
        if (draft.Snapshot is not null || draft.Output != OutputKind.Clean)
            return (null, null);

        var units = draft.Units;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.Clean.Text is not null || unit.Clean.Error.Length > 0)
                continue;

            // Order is preserved: if the oldest waiting sentence is held back, nothing behind it can be eligible.
            var continues = i < units.Count - 1 || draft.Pending.Length > 0;
            var waited = now - _firstSeen.GetValueOrDefault(unit.Id, now);
            if (!continues && draft.Recording && waited < _holdback)
                return (null, _holdback - waited);

            return ((draft.TicketFor(i), new CleanupRequest(unit.Raw, PrecedingOf(draft, i), continues, _glossary)), null);
        }

        return (null, null);
    }

    private string PrecedingOf(Draft draft, int index)
    {
        var parts = new List<(string, string, string)>();
        for (var i = Math.Max(0, index - _contextUnits); i < index; i++)
            parts.Add(("", draft.Units[i].Clean.Text ?? draft.Units[i].Raw, ""));
        return TextUnits.Join(parts);
    }

    public async ValueTask DisposeAsync()
    {
        Task worker;
        lock (_gate)
        {
            _disposed = true;
            worker = _worker;
        }

        _session.Changed -= OnChanged;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _holdbackTimer.DisposeAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
