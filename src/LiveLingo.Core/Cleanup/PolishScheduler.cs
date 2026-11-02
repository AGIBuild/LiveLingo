using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// The polish of a finished dictation. While the user speaks, each passage is cleaned on its own, so a thought that
/// the recognizer cut at a pause stays cut at the passage seam. When recording stops, runs of neighbouring passages
/// are given to the cleaner as one larger text, and a result that passes the <see cref="FidelityGate"/> replaces them
/// with a single unit (its original text is the passages' joined). Until then the per-passage results are what the user
/// sees and may write; a polish that fails, is refused, or arrives after the draft was frozen changes nothing.
/// </summary>
public sealed class PolishScheduler : IAsyncDisposable
{
    /// <summary>The most original text one polish handles: what the cleaner answers within its token limit.</summary>
    public const int MaxGroupChars = 400;

    private readonly DraftSession _session;
    private readonly ITextCleaner _cleaner;
    private readonly IReadOnlyList<string> _glossary;
    private readonly int _contextChars;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<int> _polished = [];
    private readonly HashSet<string> _given = [];

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

            var recording = _session.Read(draft => draft.Recording);
            if (recording)
                _wasRecording = true;
            else if (_wasRecording)
            {
                _wasRecording = false;
                _pending = true;
            }

            if (_pending && !_running)
            {
                _running = true;
                _worker = Task.Run(RunAsync);
            }
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
                    _running = false;
                    return;
                }

                _given.Add(Key(next.Value.Ids));
            }

            try
            {
                var text = (await _cleaner.CleanAsync(next.Value.Request, _stop.Token).ConfigureAwait(false)).Trim();
                if (FidelityGate.Check(next.Value.Request.Target, text, _glossary).Accepted)
                    ApplyPolish(next.Value.Ids, text);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                lock (_gate)
                    _running = false;
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                // The polish is an improvement on a result the user already has; the passages stay as they are.
            }
        }
    }

    private void ApplyPolish(List<int> ids, string text)
    {
        var polished = 0;
        _session.Mutate(draft => polished = draft.Collapse(ids, text) ?? 0);
        if (polished != 0)
            lock (_gate)
                _polished.Add(polished);
    }

    /// <summary>
    /// The first run of at least two cleaned passages, up to <see cref="MaxGroupChars"/> of original text, that was not
    /// polished or tried yet. Passages that are still being cleaned, edited by hand, or carry layout end a run.
    /// </summary>
    private (List<int> Ids, CleanupRequest Request)? Find(Draft draft)
    {
        if (draft.Snapshot is not null || draft.Recording || draft.Output != OutputKind.Clean)
            return null;

        var run = new List<int>();
        var length = 0;
        var units = draft.Units;
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

            if (run.Count >= 2 && !_given.Contains(Key(run)))
            {
                var first = i - run.Count;
                var target = TextUnits.Join(units.Skip(first).Take(run.Count).Select(u => ("", u.Raw, "")));
                return (run, new CleanupRequest(target, draft.PrecedingText(first, _contextChars), false, _glossary));
            }

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
