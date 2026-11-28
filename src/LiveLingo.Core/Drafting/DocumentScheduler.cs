using Microsoft.Extensions.Logging;

namespace LiveLingo.Core.Drafting;

/// <summary>One version-guarded final job, shared by polish and translation. Preview schedulers remain unit-backed.</summary>
internal sealed class DocumentScheduler : IAsyncDisposable
{
    private static readonly TimeSpan StreamThrottle = TimeSpan.FromMilliseconds(32);

    private readonly DraftSession _session;
    private readonly UnitKind _kind;
    private readonly Func<DocumentWork, Action<string>?, CancellationToken, Task<DocumentOutput>> _produce;
    private readonly Func<Draft, bool>? _holistic;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _flight;
    private DocumentTicket? _ticket;
    private int _seenEdit = -1;
    private int _seenEpoch = -1;
    private int _seenModeSwitch = -1;
    private DateTimeOffset _editedAt = DateTimeOffset.MinValue;
    private Task _worker = Task.CompletedTask;
    private bool _running;
    private bool _disposed;

    internal DocumentScheduler(DraftSession session, UnitKind kind,
        Func<DocumentWork, CancellationToken, Task<string>> produce, TimeProvider? time = null,
        Func<Draft, bool>? holistic = null,
        ILogger? logger = null)
        : this(session, kind, async (work, _, ct) => new DocumentOutput(await produce(work, ct).ConfigureAwait(false)), time, holistic, logger)
    {
    }

    internal DocumentScheduler(DraftSession session, UnitKind kind,
        Func<DocumentWork, CancellationToken, Task<DocumentOutput>> produce, TimeProvider? time = null,
        Func<Draft, bool>? holistic = null,
        ILogger? logger = null)
        : this(session, kind, (work, _, ct) => produce(work, ct), time, holistic, logger)
    {
    }

    internal DocumentScheduler(DraftSession session, UnitKind kind,
        Func<DocumentWork, Action<string>?, CancellationToken, Task<DocumentOutput>> produce, TimeProvider? time = null,
        Func<Draft, bool>? holistic = null,
        ILogger? logger = null)
    {
        _session = session;
        _kind = kind;
        _produce = produce;
        _holistic = holistic;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _session.Changed += OnChanged;
        OnChanged();
    }

    private TimeSpan Delay(Draft draft)
    {
        if (_seenEdit != draft.EditRevision || _seenEpoch != draft.Epoch)
        {
            _seenEdit = draft.EditRevision;
            _seenEpoch = draft.Epoch;
            _editedAt = draft.EditRevision > 0 ? _time.GetUtcNow() : DateTimeOffset.MinValue;
        }
        if (_seenModeSwitch != draft.ModeSwitchRevision)
        {
            _seenModeSwitch = draft.ModeSwitchRevision;
            _editedAt = DateTimeOffset.MinValue;
        }
        if (draft.Recording)
            _editedAt = DateTimeOffset.MinValue;
        return TimeSpan.FromMilliseconds(400) - (_time.GetUtcNow() - _editedAt);
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var due = _session.Read(draft =>
            {
                var delay = Delay(draft);
                if (_ticket is { } ticket && !draft.IsCurrent(ticket))
                    _flight?.Cancel();
                if (!draft.DocumentDue(_kind))
                    return false;
                if (delay > TimeSpan.Zero)
                {
                    _timer.Change(delay, Timeout.InfiniteTimeSpan);
                    return false;
                }
                return true;
            });
            if (!due || _running)
                return;
            _running = true;
            _worker = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            DocumentWork? work = null;
            DocumentTicket ticket;
            CancellationTokenSource cancel;
            Exception? captureError = null;
            lock (_gate)
            {
                var next = _disposed ? null : _session.Read(draft =>
                    draft.DocumentDue(_kind) && Delay(draft) <= TimeSpan.Zero
                        ? (DocumentTicket?)draft.DocumentTicketFor(_kind) : null);
                if (next is null)
                {
                    _running = false;
                    return;
                }
                ticket = next.Value;
                _ticket = ticket;
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _flight = cancel;
                try
                {
                    work = _session.Read(draft =>
                        draft.IsCurrent(ticket) ? DocumentWork.Capture(draft, _kind, _holistic?.Invoke(draft) == true) : null);
                }
                catch (Exception ex) { captureError = ex; }
            }
            try
            {
                if (captureError is not null)
                    throw captureError;
                if (work is null)
                    continue;
                long lastStreamTicks = 0;
                void ReportPartial(string partial)
                {
                    if (cancel.IsCancellationRequested || string.IsNullOrEmpty(partial))
                        return;
                    var now = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (lastStreamTicks != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(lastStreamTicks) < StreamThrottle)
                        return;
                    lastStreamTicks = now;
                    _session.MutateIf(draft => !cancel.IsCancellationRequested && draft.StreamDocument(ticket, partial));
                }

                var output = await _produce(work, ReportPartial, cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(output.Text))
                    throw new InvalidDataException("Empty final document response.");
                _session.Mutate(draft =>
                {
                    if (!cancel.IsCancellationRequested)
                        draft.CompleteDocument(ticket, output.Text, warning: output.Warning);
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception ex)
            {
                var err = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                _logger?.LogWarning("Final {Kind} document failed (mode={Mode}): {Error}", _kind, ticket.PolishModeId, err);
                _session.Mutate(draft =>
                {
                    if (!cancel.IsCancellationRequested)
                        draft.CompleteDocument(ticket, null, err);
                });
            }
            finally
            {
                lock (_gate)
                {
                    _flight = null;
                    _ticket = null;
                }
                cancel.Dispose();
            }
        }
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
        await _timer.DisposeAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
