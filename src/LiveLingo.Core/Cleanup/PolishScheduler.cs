using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveLingo.Core.Cleanup;

/// <summary>
/// The polish of a finished dictation. While the user speaks, each passage is cleaned on its own
/// (<see cref="CleanupScheduler"/>), so a thought that the recognizer cut at a pause stays cut at the passage seam.
/// When recording stops, the whole dictation is polished as one text: runs of neighbouring passages (up to
/// <see cref="MaxGroupChars"/> of original text; usually the whole dictation) go to the polisher, and a result that passes
/// the <see cref="FidelityGate"/> replaces the run with a single unit (its original text is the passages' joined, so the
/// user can still go back to it). The polish starts at once, from the original text, without waiting for the
/// per-passage cleanups, which pause meanwhile.
/// <list type="bullet">
/// <item>The dictation is summarized first; every run reads that summary alongside the original text.</item>
/// <item>An invalid custom result is retried once with default requirements. No recursive model retries.</item>
/// <item>Until the polish is done the draft counts as processing (<see cref="Draft.Polishing"/>, set in the very step that
/// recording stops), so writing waits for it. A polish that fails or arrives after the draft was frozen changes nothing.</item>
/// </list>
/// </summary>
public sealed class PolishScheduler : IAsyncDisposable
{
    /// <summary>The most original text one polish handles; it still fits the model's context and output limit.</summary>
    public const int MaxGroupChars = 800;

    private readonly DraftSession _session;
    private readonly IDictationPolisher _polisher;
    private readonly IReadOnlyList<string> _glossary;
    private readonly string _style;
    private readonly int _contextChars;
    private readonly ILogger<PolishScheduler> _logger;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<int> _polished = [];
    private readonly HashSet<string> _tried = [];

    private string? _summary;
    private int _revision = -1;
    private int _sourceRevision = -1;
    private CancellationTokenSource? _flight;
    private bool _running;
    private bool _disposed;
    private Task _worker = Task.CompletedTask;

    /// <param name="style">The polish requirements, already resolved (<see cref="PolishStyle.Resolve"/>).</param>
    public PolishScheduler(
        DraftSession session, IDictationPolisher polisher, IReadOnlyList<string> glossary, string style, int contextChars,
        ILogger<PolishScheduler>? logger = null)
    {
        _session = session;
        _polisher = polisher;
        _glossary = glossary;
        _style = PolishStyle.Resolve(style);
        _contextChars = contextChars;
        _logger = logger ?? NullLogger<PolishScheduler>.Instance;
        _session.Mutate(draft => draft.PolishExpected = true);
        _session.Changed += OnChanged;
        OnChanged();
    }

    private void OnChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var due = _session.Read(Observe);
            if (_running || !due)
                return;

            _running = true;
            _worker = Task.Run(RunAsync);
        }
    }

    /// <summary>A recording just stopped and the polish is not done: only for the cleanup output, and not for a frozen draft.</summary>
    private static bool IsDue(Draft draft) =>
        draft.Polishing && !draft.Recording && draft.Snapshot is null && draft.Output == OutputKind.Clean;

    private bool Observe(Draft draft)
    {
        if (_revision != draft.DictationRevision || _sourceRevision != draft.SourceRevision)
        {
            _flight?.Cancel();
            _revision = draft.DictationRevision;
            _sourceRevision = draft.SourceRevision;
            _tried.Clear();
            _polished.Clear();
            _summary = null;
        }
        var due = IsDue(draft);
        if (!due)
            _flight?.Cancel();
        return due;
    }

    private abstract record Step;

    private sealed record Summarize(string Text, int Revision, int SourceRevision) : Step;

    private sealed record Polish(List<int> Ids, string Target, string Preceding, int Revision, int SourceRevision) : Step;

    private sealed record Idle : Step;

    private sealed record Done(int Revision, int SourceRevision) : Step;

    private async Task RunAsync()
    {
        while (true)
        {
            Step step;
            CancellationTokenSource cancel;
            lock (_gate)
            {
                step = _disposed ? new Idle() : _session.Read(draft =>
                {
                    Observe(draft);
                    return Next(draft);
                });
                switch (step)
                {
                    case Idle:
                        _summary = null;
                        _running = false;
                        return;
                    case Done:
                        if (Finish(step))
                            return;
                        continue;
                }

                if (step is Polish polish)
                    _tried.Add(Key(polish.Ids));
                cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _flight = cancel;
            }

            try
            {
                if (step is Summarize summarize)
                {
                    var summary = (await _polisher.SummarizeAsync(summarize.Text, cancel.Token).ConfigureAwait(false)).Trim();
                    lock (_gate)
                        if (!cancel.IsCancellationRequested && _revision == summarize.Revision)
                            _summary = summary;
                }
                else
                    await PolishAsync((Polish)step, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A new recording, source edit or output choice superseded this request.
            }
            catch (Exception ex) when (ServiceFailure.IsUnavailable(ex))
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogWarning("Final polish service unavailable ({Failure}); keeping passage results.", ex.GetType().Name);
                Report(PolishOutcome.KeptPreview);
                lock (_gate)
                    if (Finish(step, cancel.Token))
                        return;
                continue;
            }
            catch (InvalidDataException)
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogWarning("Incomplete final polish response; keeping passage results.");
                if (step is Summarize)
                {
                    lock (_gate)
                        if (!cancel.IsCancellationRequested)
                            _summary = "";
                }
                else
                    Report(PolishOutcome.KeptPreview);
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested)
                    continue;
                _logger.LogError("Final polish failed ({Failure}); keeping passage results.", ex.GetType().Name);
                Report(PolishOutcome.KeptPreview);
                lock (_gate)
                    if (Finish(step, cancel.Token))
                        return;
                continue;
            }
            finally
            {
                lock (_gate)
                {
                    if (cancel.IsCancellationRequested && step is Polish cancelled)
                        _tried.Remove(Key(cancelled.Ids));
                    _flight = null;
                }
                cancel.Dispose();
            }
        }
    }

    private async Task PolishAsync(Polish polish, CancellationToken ct)
    {
        var request = new PolishRequest(polish.Target, polish.Preceding, _summary ?? "", _glossary, _style);
        string? text = null;
        try
        {
            text = (await _polisher.PolishAsync(request, ct).ConfigureAwait(false)).Trim();
            ct.ThrowIfCancellationRequested();
        }
        catch (InvalidDataException) when (_style != PolishStyle.Default)
        {
            _logger.LogWarning("Custom polish response incomplete; retrying default requirements.");
        }

        var gate = FidelityGate.Check(polish.Target, text ?? "", _glossary, allowParagraphs: true);
        if (!gate.Accepted && _style != PolishStyle.Default)
        {
            _logger.LogWarning("Custom polish refused ({Reason}); retrying default requirements.", gate.Rejection?.Reason);
            Report(PolishOutcome.DefaultRequirements);
            text = (await _polisher.PolishAsync(request with { Style = PolishStyle.Default }, ct).ConfigureAwait(false)).Trim();
            ct.ThrowIfCancellationRequested();
            gate = FidelityGate.Check(polish.Target, text, _glossary, allowParagraphs: true);
        }
        if (!gate.Accepted)
        {
            _logger.LogWarning("Final polish refused ({Reason}); keeping passage results.", gate.Rejection?.Reason);
            Report(PolishOutcome.KeptPreview);
            return;
        }

        var id = _session.Mutate(draft =>
            !ct.IsCancellationRequested && IsDue(draft) && draft.DictationRevision == polish.Revision && draft.SourceRevision == polish.SourceRevision
                ? draft.Collapse(polish.Ids, text!)
                : null) ?? 0;
        if (id != 0)
            lock (_gate)
                _polished.Add(id);
    }

    /// <summary>The polish is over for this stop: the draft no longer waits for it. Called with the gate held.</summary>
    private bool Finish(Step step, CancellationToken ct = default)
    {
        var (revision, sourceRevision) = step switch
        {
            Polish p => (p.Revision, p.SourceRevision),
            Summarize s => (s.Revision, s.SourceRevision),
            Done d => (d.Revision, d.SourceRevision),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        var finished = _session.Mutate(draft =>
        {
            if (ct.IsCancellationRequested || !IsDue(draft) ||
                draft.DictationRevision != revision || draft.SourceRevision != sourceRevision)
                return false;
            draft.Polishing = false;
            return true;
        });
        if (!finished)
            return false;
        _summary = null;
        _running = false;
        return true;
    }

    private void Report(PolishOutcome outcome) =>
        _session.Mutate(draft =>
        {
            if (_flight is { IsCancellationRequested: false } && IsDue(draft) &&
                draft.DictationRevision == _revision && draft.SourceRevision == _sourceRevision &&
                draft.PolishOutcome != PolishOutcome.KeptPreview)
                draft.PolishOutcome = outcome;
        });

    private Step Next(Draft draft)
    {
        if (!IsDue(draft))
            return new Idle();

        var runs = Runs(draft);
        foreach (var (first, count) in runs)
        {
            if (Untried(draft, first, count) is not { } polish)
                continue;

            if (_summary is null)
                return new Summarize(draft.SourceText(), draft.DictationRevision, draft.SourceRevision);

            return polish;
        }

        return new Done(draft.DictationRevision, draft.SourceRevision);
    }

    /// <summary>
    /// Runs of neighbouring passages that can be polished together: not edited by hand, without layout, not polished yet,
    /// together up to <see cref="MaxGroupChars"/> of original text. A single passage is no run: there is nothing to join.
    /// </summary>
    private List<(int First, int Count)> Runs(Draft draft)
    {
        var runs = new List<(int, int)>();
        var units = draft.Units;
        var first = 0;
        var length = 0;
        for (var i = 0; i <= units.Count; i++)
        {
            var polishable = i < units.Count && IsPolishable(units[i]);
            if (polishable && (i == first || length + units[i].Raw.Length <= MaxGroupChars))
            {
                length += units[i].Raw.Length;
                continue;
            }

            // The run ends before this unit, which starts the next run when it can be polished.
            if (i - first >= 1)
                runs.Add((first, i - first));
            first = polishable ? i : i + 1;
            length = polishable ? units[i].Raw.Length : 0;
        }

        return runs;
    }

    /// <summary>Each unchanged run is attempted only once.</summary>
    private Polish? Untried(Draft draft, int first, int count)
    {
        if (count < 1)
            return null;

        var ids = draft.Units.Skip(first).Take(count).Select(unit => unit.Id).ToList();
        if (!_tried.Contains(Key(ids)))
        {
            var target = TextUnits.Join(draft.Units.Skip(first).Take(count).Select(unit => ("", unit.Raw, "")));
            return new Polish(ids, target, draft.PrecedingText(first, _contextChars), draft.DictationRevision, draft.SourceRevision);
        }

        return null;
    }

    private bool IsPolishable(DraftUnit unit) =>
        !unit.Clean.IsOwned && !unit.Translation.IsOwned && unit.Lead.Length == 0 && unit.Trail.Length == 0
        && unit.Raw.Length <= MaxGroupChars && !_polished.Contains(unit.Id);

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
