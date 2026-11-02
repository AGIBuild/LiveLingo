using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// Cleans the units of a <see cref="DraftSession"/> while the user is still speaking: one request at a time,
/// always for the oldest unit that has none. Dictated units are whole passages, closed by a long pause, so a unit
/// is cleaned as soon as it exists. While the user types, the last unit is cleaned only after a short pause in
/// typing, not once per key. A result that fails the <see cref="FidelityGate"/> keeps the original and records the reason.
/// </summary>
public sealed class CleanupScheduler : IAsyncDisposable
{
    private readonly DraftSession _session;
    private readonly ITextCleaner _cleaner;
    private readonly IReadOnlyList<string> _glossary;
    private readonly int _contextChars;
    private readonly TimeProvider _time;
    /// <summary>How long the source must be still before typed text is cleaned: shorter than a sentence, longer than a keystroke.</summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ITimer _typingTimer;

    private int _seenEditRevision;
    private DateTimeOffset _editedAt = DateTimeOffset.MinValue;
    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;
    private (JobTicket Ticket, CancellationTokenSource Cancel)? _inFlight;

    public CleanupScheduler(
        DraftSession session,
        ITextCleaner cleaner,
        IReadOnlyList<string> glossary,
        int contextChars,
        TimeProvider? time = null)
    {
        _session = session;
        _cleaner = cleaner;
        _glossary = glossary;
        _contextChars = contextChars;
        _time = time ?? TimeProvider.System;
        _typingTimer = _time.CreateTimer(_ => Wake(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

            ObserveEdits(_time.GetUtcNow());

            if (_inFlight is { } flight && _session.Read(draft => draft.Polishing || !draft.IsCurrent(flight.Ticket, UnitKind.Clean)))
                flight.Cancel.Cancel();

            StartWorkerIfNeeded();
        }
    }

    /// <summary>
    /// Notes when the user last edited the text. Also called before every scan, because a scan can see an edit before
    /// its change notification arrives; without it that edit would count as made long ago and skip the typing pause.
    /// </summary>
    private void ObserveEdits(DateTimeOffset now)
    {
        var revision = _session.Read(draft => draft.EditRevision);
        if (revision == _seenEditRevision)
            return;

        _seenEditRevision = revision;
        _editedAt = now;
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
                if (!_disposed)
                    ObserveEdits(_time.GetUtcNow());
                var (next, wait) = _disposed ? (null, null) : _session.Read(draft => Find(draft, _time.GetUtcNow()));
                if (next is null)
                {
                    // Once disposed the timer must not be touched: changing a disposed timer throws.
                    if (!_disposed)
                        _typingTimer.Change(wait ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _running = false;
                    return;
                }

                (ticket, request) = next.Value;
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _inFlight = (ticket, cancel);
            }

            try
            {
                var (text, rejection) = await CleanUnitAsync(request, cancel.Token).ConfigureAwait(false);
                _session.Mutate(draft => draft.Complete(ticket, UnitKind.Clean, text, rejection));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // The sentence changed or the scheduler is stopping; the next scan decides what to do.
            }
            catch (Exception ex) when (ServiceFailure.IsUnavailable(ex))
            {
                _session.Mutate(draft => draft.FailPending(ticket, UnitKind.Clean, ex.Message));
            }
            catch (InvalidDataException)
            {
                // A truncated answer says nothing about the user's words: keep them and write on, like any refused cleanup.
                _session.Mutate(draft => draft.Complete(ticket, UnitKind.Clean, request.Target, new GateRejection(GateReason.Incomplete)));
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

    /// <summary>
    /// Cleans a unit and checks the result. A passage that fails the check is cleaned again sentence by sentence, each
    /// sentence checked on its own, so one bad sentence does not cost the rest of the passage its cleanup. The unit
    /// is refused (rejection set, original kept) only when nothing in it passed.
    /// </summary>
    private async Task<(string Text, GateRejection? Rejection)> CleanUnitAsync(CleanupRequest request, CancellationToken ct)
    {
        var cleaned = (await _cleaner.CleanAsync(request, ct).ConfigureAwait(false)).Trim();
        var gate = FidelityGate.Check(request.Target, cleaned, _glossary);
        if (gate.Accepted)
            return (cleaned, null);

        // A multi-line unit is a document whose layout would be lost by splitting it.
        if (request.Target.Contains('\n'))
            return (request.Target, gate.Rejection);

        var sentences = TextUnits.Split(request.Target);
        if (sentences.Count < 2)
            return (request.Target, gate.Rejection);

        var parts = new List<string>();
        var anyCleaned = false;
        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i].Body;
            var preceding = TextUnits.Join(parts.Prepend(request.Preceding).Select(part => ("", part, "")));
            var one = new CleanupRequest(sentence, preceding, i < sentences.Count - 1 || request.Continues, request.Glossary);
            var text = (await _cleaner.CleanAsync(one, ct).ConfigureAwait(false)).Trim();
            var accepted = FidelityGate.Check(sentence, text, _glossary).Accepted;
            anyCleaned |= accepted;
            parts.Add(accepted ? text : sentence);
        }

        return anyCleaned
            ? (TextUnits.Join(parts.Select(part => ("", part, ""))), null)
            : (request.Target, gate.Rejection);
    }

    /// <summary>The next unit to clean, or how long typing must stay still before the last unit is cleaned.</summary>
    private ((JobTicket Ticket, CleanupRequest Request)? Next, TimeSpan? Wait) Find(Draft draft, DateTimeOffset now)
    {
        // The polish of the whole dictation reads the original text and goes first; units it could not polish are cleaned after it.
        if (draft.Snapshot is not null || draft.Output != OutputKind.Clean || draft.Polishing)
            return (null, null);

        var units = draft.Units;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.Clean.Text is not null || unit.Clean.Error.Length > 0)
                continue;

            // Order is preserved: if the oldest waiting unit has to wait, nothing behind it can be eligible.
            // While the microphone is open the speaker may always go on, even when the last passage ended in a long pause.
            var continues = i < units.Count - 1 || draft.Pending.Length > 0 || draft.Recording;
            var typing = TypingPause - (now - _editedAt);
            if (!continues && typing > TimeSpan.Zero)
                return (null, typing);

            return ((draft.TicketFor(i), new CleanupRequest(unit.Raw, draft.PrecedingText(i, _contextChars), continues, _glossary)), null);
        }

        return (null, null);
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
        await _typingTimer.DisposeAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
