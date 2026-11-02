using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// The polish of a finished dictation. While the user speaks, each passage is cleaned on its own, so a thought that
/// the recognizer cut at a pause stays cut at the passage seam. When recording stops, runs of neighbouring passages
/// are given to the cleaner as one larger text, and a result that passes the <see cref="FidelityGate"/> replaces them
/// with a single unit (its original text is the passages' joined, so the user can still go back to it).
/// A run the gate refuses is split in halves and each half is tried on its own: one misheard word costs only the half
/// it is in, not the polish of the whole dictation. Until the polish is done the draft counts as processing
/// (<see cref="Draft.Polishing"/>), so writing waits for it; a polish that fails, or arrives after the draft was
/// frozen, changes nothing.
/// </summary>
public sealed class PolishScheduler : IAsyncDisposable
{
    /// <summary>The most original text one polish handles; it still fits the cleaner's context and output limit.</summary>
    public const int MaxGroupChars = 800;

    private readonly DraftSession _session;
    private readonly ITextCleaner _cleaner;
    private readonly IReadOnlyList<string> _glossary;
    private readonly int _contextChars;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<int> _polished = [];
    private readonly HashSet<string> _tried = [];

    private bool _wasRecording;
    private bool _pending;
    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;

    public PolishScheduler(DraftSession session, ITextCleaner cleaner, IReadOnlyList<string> glossary, int contextChars)
    {
        _session = session;
        _cleaner = cleaner;
        _glossary = glossary;
        _contextChars = contextChars;
        _session.Changed += OnChanged;
        OnChanged();
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_session.Read(draft => draft.Recording))
                _wasRecording = true;
            else if (_wasRecording)
            {
                _wasRecording = false;
                _pending = true;
            }

            if (!_pending || _running || _session.Read(Find) is null)
                return;

            // Before the worker starts, so writing never gets a moment in which the dictation looks finished.
            // Marked running first: telling the draft calls this handler again (inline when no context is set).
            _running = true;
            _session.Mutate(draft => draft.Polishing = true);
            _worker = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            (List<int> Ids, CleanupRequest Request)? next;
            lock (_gate)
            {
                next = _disposed ? null : _session.Read(Find);
                if (next is null)
                {
                    _session.Mutate(draft => draft.Polishing = false);
                    _running = false;
                    return;
                }

                _tried.Add(Key(next.Value.Ids));
            }

            try
            {
                var text = (await _cleaner.CleanAsync(next.Value.Request, _stop.Token).ConfigureAwait(false)).Trim();
                if (FidelityGate.Check(next.Value.Request.Target, text, _glossary).Accepted)
                    ApplyPolish(next.Value.Ids, text);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                Finish();
                return;
            }
            catch (Exception ex) when (ServiceFailure.IsUnavailable(ex))
            {
                // Nothing is going to answer; the passages stay as they are.
                Finish();
                return;
            }
            catch (InvalidDataException)
            {
                // A truncated answer: a smaller run may still fit.
            }
        }
    }

    /// <summary>Ends the polish for good: the draft is no longer waiting for it.</summary>
    private void Finish()
    {
        lock (_gate)
        {
            _pending = false;
            _session.Mutate(draft => draft.Polishing = false);
            _running = false;
        }
    }

    private void ApplyPolish(List<int> ids, string text)
    {
        var polished = _session.Mutate(draft => draft.Collapse(ids, text)) ?? 0;
        if (polished != 0)
            lock (_gate)
                _polished.Add(polished);
    }

    /// <summary>
    /// The next run to polish. Neighbouring passages that are cleaned, not edited by hand, without layout and not polished
    /// yet form runs of up to <see cref="MaxGroupChars"/> of original text; within a run that was already tried (and
    /// refused) the halves are tried instead, down to two passages.
    /// </summary>
    private (List<int> Ids, CleanupRequest Request)? Find(Draft draft)
    {
        if (draft.Snapshot is not null || draft.Recording || draft.Output != OutputKind.Clean)
            return null;

        var units = draft.Units;
        // The whole dictation is polished together, so wait until every passage has its own cleanup.
        if (units.Any(unit => unit.Clean.Text is null && unit.Clean.Error.Length == 0))
            return null;

        var run = new List<int>();
        var length = 0;
        for (var i = 0; i <= units.Count; i++)
        {
            var unit = i < units.Count ? units[i] : null;
            var fits = unit is not null && IsPolishable(unit) && (run.Count == 0 || length + unit.Raw.Length <= MaxGroupChars);
            if (fits)
            {
                run.Add(unit!.Id);
                length += unit.Raw.Length;
                continue;
            }

            if (Untried(draft, i - run.Count, run.Count) is { } found)
                return found;

            run.Clear();
            length = 0;
            if (unit is not null && IsPolishable(unit))
            {
                run.Add(unit.Id);
                length = unit.Raw.Length;
            }
        }

        return null;
    }

    /// <summary>The run at <paramref name="first"/>, or the first untried half of it when it was tried already.</summary>
    private (List<int> Ids, CleanupRequest Request)? Untried(Draft draft, int first, int count)
    {
        if (count < 2)
            return null;

        var ids = draft.Units.Skip(first).Take(count).Select(unit => unit.Id).ToList();
        if (!_tried.Contains(Key(ids)))
        {
            var target = TextUnits.Join(draft.Units.Skip(first).Take(count).Select(unit => ("", unit.Raw, "")));
            return (ids, new CleanupRequest(target, draft.PrecedingText(first, _contextChars), false, _glossary));
        }

        if (count < 4)
            return null;

        var half = count / 2;
        return Untried(draft, first, half) ?? Untried(draft, first + half, count - half);
    }

    private bool IsPolishable(DraftUnit unit) =>
        unit.Clean.Text is not null && unit.Clean.Error.Length == 0 && !unit.Clean.IsOwned
        && unit.Lead.Length == 0 && unit.Trail.Length == 0 && unit.Raw.Length <= MaxGroupChars
        && !_polished.Contains(unit.Id);

    private static string Key(IEnumerable<int> ids) => string.Join(',', ids);

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
        await worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
