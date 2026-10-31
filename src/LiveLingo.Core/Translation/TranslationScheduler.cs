using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;

namespace LiveLingo.Core.Translation;

/// <summary>
/// Keeps the translations of a <see cref="DraftSession"/> up to date while the user writes or speaks:
/// one request at a time, always for the first unit that still needs a translation, and a request is cancelled
/// as soon as its unit is edited, removed or invalidated. A trailing unfinished sentence is translated only
/// after the source has been idle for <paramref name="tailDelay"/>.
/// </summary>
public sealed class TranslationScheduler : IAsyncDisposable
{
    private readonly DraftSession _session;
    private readonly ITranslator _translator;
    private readonly TimeProvider _time;
    private readonly TimeSpan _tailDelay;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ITimer _tailTimer;

    private TargetLanguage _target;
    private int _seenSourceRevision = -1;
    private int _flushRevision = -1;
    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;
    private (JobTicket Ticket, CancellationTokenSource Cancel)? _inFlight;

    public TranslationScheduler(
        DraftSession session,
        ITranslator translator,
        TargetLanguage target,
        TimeSpan tailDelay,
        TimeProvider? time = null)
    {
        _session = session;
        _translator = translator;
        _target = target;
        _tailDelay = tailDelay;
        _time = time ?? TimeProvider.System;
        _tailTimer = _time.CreateTimer(_ => FlushTail(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _session.Changed += OnChanged;
    }

    public TargetLanguage Target
    {
        get
        {
            lock (_gate)
                return _target;
        }
    }

    /// <summary>Changes the target language; every translation of the draft is dropped and produced again.</summary>
    public void SetTarget(TargetLanguage target)
    {
        lock (_gate)
        {
            if (_target == target)
                return;
            _target = target;
        }

        _session.Mutate(draft => draft.InvalidateTranslations());
    }

    /// <summary>Translates the unfinished trailing unit now (for example when the user asks to write out the result).</summary>
    public void FlushTail()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _flushRevision = _session.Read(draft => draft.SourceRevision);
            StartWorkerIfNeeded();
        }
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var revision = _session.Read(draft => draft.SourceRevision);
            if (revision != _seenSourceRevision)
            {
                _seenSourceRevision = revision;
                _tailTimer.Change(_tailDelay, Timeout.InfiniteTimeSpan);
            }

            if (_inFlight is { } flight && !_session.Read(draft => draft.IsCurrent(flight.Ticket, UnitKind.Translation)))
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
            string source;
            TargetLanguage target;
            CancellationTokenSource cancel;
            lock (_gate)
            {
                var next = _disposed ? null : _session.Read(FindNext);
                if (next is null)
                {
                    _running = false;
                    return;
                }

                (ticket, source) = next.Value;
                target = _target;
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _inFlight = (ticket, cancel);
            }

            try
            {
                var translated = await _translator
                    .TranslateAsync(new TranslationRequest(source, target, []), cancel.Token)
                    .ConfigureAwait(false);
                _session.Mutate(draft => draft.Complete(ticket, UnitKind.Translation, translated.Text));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // The unit changed or the scheduler is stopping; the next scan decides what to do.
            }
            catch (Exception ex) when (IsServiceFailure(ex))
            {
                // The service is unavailable for every unit: fail them together instead of trying each one in turn.
                _session.Mutate(draft => FailPending(draft, ticket, ex.Message));
            }
            catch (Exception ex)
            {
                _session.Mutate(draft => draft.Fail(ticket, UnitKind.Translation, ex.Message));
            }
            finally
            {
                lock (_gate)
                    _inFlight = null;
                cancel.Dispose();
            }
        }
    }

    private static bool IsServiceFailure(Exception ex) =>
        ex is HttpRequestException or LlamaServerStartException or ModelNotReadyException;

    private static void FailPending(Draft draft, JobTicket current, string reason)
    {
        draft.Fail(current, UnitKind.Translation, reason);
        for (var i = 0; i < draft.Units.Count; i++)
        {
            var result = draft.Units[i].Translation;
            if (result.Text is null && result.Error.Length == 0)
                draft.Fail(draft.TicketFor(i), UnitKind.Translation, reason);
        }
    }

    private (JobTicket Ticket, string Source)? FindNext(Draft draft)
    {
        if (draft.Snapshot is not null || draft.Output != OutputKind.Translation)
            return null;

        var flushTail = draft.SourceRevision == _flushRevision;
        for (var i = 0; i < draft.Units.Count; i++)
        {
            var unit = draft.Units[i];
            var needsTranslation = unit.Translation.Text is null && unit.Translation.Error.Length == 0;
            if (needsTranslation && unit.Raw.Length > 0 && (unit.IsComplete || flushTail))
                return (draft.TicketFor(i), unit.Raw);
        }

        return null;
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
        await _tailTimer.DisposeAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
