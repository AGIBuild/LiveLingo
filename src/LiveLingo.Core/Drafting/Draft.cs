namespace LiveLingo.Core.Drafting;

/// <summary>
/// The single model of what the user is composing: ordered source units, each with an independent cleanup
/// and translation result, plus the rules that decide when the text may be written out.
/// Not thread-safe; one serial owner drives it and results arrive through <see cref="JobTicket"/>s.
/// </summary>
public sealed class Draft
{
    private readonly List<DraftUnit> _units = [];
    private int _nextUnitId;
    private int _epoch;
    private int _translationRevision;

    public Draft(bool hasTarget) => Reset(hasTarget);

    public IReadOnlyList<DraftUnit> Units => _units;
    public OutputKind Output { get; set; }
    public DraftRoute Route { get; private set; }
    public bool HasTarget { get; private set; }
    public bool TargetLost { get; set; }
    public bool Recording { get; set; }

    /// <summary>
    /// Speech that has been recognized but not settled into units yet (the live hypothesis). It is shown after the
    /// units and is never part of <see cref="Text"/>; the dictation session replaces it as recognition advances.
    /// </summary>
    public string Pending { get; private set; } = "";

    /// <summary>Increases whenever the source text changes (not when results arrive).</summary>
    public int SourceRevision { get; private set; }

    /// <summary>Increases whenever the user edits the source text by hand (typing, pasting), not when speech adds a sentence.</summary>
    public int EditRevision { get; private set; }

    /// <summary>The frozen result of a successful commit; the draft accepts no further changes once set.</summary>
    public CommitResult? Snapshot { get; private set; }

    public void Reset(bool hasTarget)
    {
        _epoch++;
        SourceRevision++;
        _translationRevision = 0;
        _units.Clear();
        Output = OutputKind.Clean;
        Route = DraftRoute.Units;
        HasTarget = hasTarget;
        TargetLost = false;
        Recording = false;
        Pending = "";
        Snapshot = null;
    }

    public void SetPending(string text)
    {
        EnsureEditable();
        Pending = text;
    }

    public JobTicket Append(string raw)
    {
        EnsureEditable();
        SourceRevision++;
        var unit = NewUnit(new SourceUnit(raw));
        _units.Add(unit);
        return TicketFor(unit);
    }

    public JobTicket TicketFor(int index) => TicketFor(_units[index]);

    /// <summary>
    /// Reconciles the units with the units of the source text. Units whose body is unchanged keep their results
    /// and manual edits; only new units return tickets for processing. A unit may hold several sentences (a dictated
    /// passage): it is kept while the same run of sentences is still in the source, and edited text falls back to sentences.
    /// </summary>
    public IReadOnlyList<JobTicket> SyncSource(IReadOnlyList<SourceUnit> source, DraftRoute route)
    {
        EnsureEditable();
        SourceRevision++;
        EditRevision++;
        var pool = new Dictionary<string, Queue<DraftUnit>>();
        foreach (var unit in _units)
        {
            if (!pool.TryGetValue(unit.Raw, out var queue))
                pool[unit.Raw] = queue = new Queue<DraftUnit>();
            queue.Enqueue(unit);
        }

        var longest = _units.Count == 0 ? 0 : _units.Max(unit => unit.Raw.Length);
        var created = new List<JobTicket>();
        var next = new List<DraftUnit>(source.Count);
        for (var start = 0; start < source.Count;)
        {
            var (reused, merged, count) = TakeRun(pool, source, start, longest);
            if (reused is not null)
            {
                reused.Apply(merged!);
                next.Add(reused);
                start += count;
                continue;
            }

            var unit = NewUnit(source[start]);
            created.Add(TicketFor(unit));
            next.Add(unit);
            start++;
        }

        _units.Clear();
        _units.AddRange(next);
        Route = route;
        return created;
    }

    /// <summary>Takes the existing unit that holds the longest run of source units starting at <paramref name="start"/>.</summary>
    private static (DraftUnit? Unit, SourceUnit? Merged, int Count) TakeRun(
        Dictionary<string, Queue<DraftUnit>> pool, IReadOnlyList<SourceUnit> source, int start, int longest)
    {
        var body = source[start].Body;
        (string Body, int Count)? found = null;
        for (var count = 1; body.Length <= longest; count++)
        {
            if (pool.TryGetValue(body, out var queue) && queue.Count > 0)
                found = (body, count);

            var following = start + count;
            if (following >= source.Count || source[following - 1].Trail.Length > 0 || source[following].Lead.Length > 0)
                break;
            body = TextUnits.Join([("", body, ""), ("", source[following].Body, "")]);
        }

        if (found is not var (foundBody, foundCount))
            return (null, null, 0);

        var last = source[start + foundCount - 1];
        var merged = new SourceUnit(foundBody, source[start].Lead, last.Trail, last.IsComplete);
        return (pool[foundBody].Dequeue(), merged, foundCount);
    }

    /// <summary>
    /// Replaces consecutive units by one unit whose original text is theirs joined and whose cleanup result is
    /// <paramref name="clean"/>; returns its id. Refused (null) when the draft is frozen, the units are no longer
    /// consecutive, carry layout, or any has a cleanup the user edited.
    /// </summary>
    public int? Collapse(IReadOnlyList<int> unitIds, string clean)
    {
        if (Snapshot is not null || unitIds.Count < 2)
            return null;

        var first = _units.FindIndex(unit => unit.Id == unitIds[0]);
        if (first < 0 || first + unitIds.Count > _units.Count)
            return null;

        var run = _units.GetRange(first, unitIds.Count);
        if (!run.Select(unit => unit.Id).SequenceEqual(unitIds)
            || run.Any(unit => unit.Clean.IsOwned || unit.Lead.Length > 0 || unit.Trail.Length > 0))
            return null;

        var merged = NewUnit(new SourceUnit(TextUnits.Join(run.Select(unit => ("", unit.Raw, ""))), IsComplete: run[^1].IsComplete));
        merged.Clean.Text = clean;
        _units.RemoveRange(first, run.Count);
        _units.Insert(first, merged);
        return merged.Id;
    }

    /// <summary>
    /// The text before a unit as far back as <paramref name="budget"/> characters allow: whole units, nearest first, and
    /// always at least the nearest one, so a thought that continues across a pause keeps its beginning. Cleaned text
    /// where there is some, the original otherwise.
    /// </summary>
    public string PrecedingText(int index, int budget)
    {
        var parts = new List<(string, string, string)>();
        var length = 0;
        for (var i = index - 1; i >= 0 && (parts.Count == 0 || length < budget); i--)
        {
            var text = _units[i].Clean.Text ?? _units[i].Raw;
            parts.Insert(0, ("", text, ""));
            length += text.Length;
        }
        return TextUnits.Join(parts);
    }

    /// <summary>Stores a model result. Returns false when the ticket is stale or the user owns the text.</summary>
    public bool Complete(JobTicket ticket, UnitKind kind, string text, GateRejection? rejection = null)
    {
        if (!TryResolve(ticket, kind, out var unit))
            return false;

        var result = unit.Result(kind);
        result.Text = rejection is null ? text : unit.Raw;
        result.Error = "";
        result.Rejection = rejection;
        return true;
    }

    public bool Fail(JobTicket ticket, UnitKind kind, string reason)
    {
        if (!TryResolve(ticket, kind, out var unit))
            return false;

        var result = unit.Result(kind);
        result.Text = null;
        result.Error = reason;
        return true;
    }

    /// <summary>True while a result for this ticket would still be accepted.</summary>
    public bool IsCurrent(JobTicket ticket, UnitKind kind) => TryResolve(ticket, kind, out _);

    /// <summary>Clears a failed or finished model result so it is produced again; user-edited text is kept.</summary>
    public void Retry(int index, UnitKind kind)
    {
        EnsureEditable();
        var result = _units[index].Result(kind);
        if (!result.IsOwned)
            result.Clear();
    }

    /// <summary>
    /// Fails the ticket's unit and every other unit that is still waiting for this kind of result, with the same
    /// reason. Used when the service itself is unavailable, so units are not attempted one by one.
    /// </summary>
    public void FailPending(JobTicket current, UnitKind kind, string reason)
    {
        if (!Fail(current, kind, reason))
            return;

        foreach (var unit in _units)
        {
            var result = unit.Result(kind);
            if (result.Text is null && result.Error.Length == 0)
                result.Error = reason;
        }
    }

    public void Edit(int index, UnitKind kind, string text)
    {
        EnsureEditable();
        var result = _units[index].Result(kind);
        result.Text = text;
        result.IsOwned = true;
        result.Error = "";
    }

    public void UseRaw(int index) => Edit(index, UnitKind.Clean, _units[index].Raw);

    /// <summary>Drops every translation and invalidates the ones still in flight (the target language changed).</summary>
    public void InvalidateTranslations()
    {
        if (Snapshot is not null)
            return;

        _translationRevision++;
        foreach (var unit in _units)
            unit.Translation.Clear();
    }

    public void Retarget(bool hasTarget)
    {
        HasTarget = hasTarget;
        TargetLost = false;
    }

    public UnitState StateOf(DraftUnit unit, OutputKind output)
    {
        if (output == OutputKind.Raw)
            return UnitState.Ready;

        var result = unit.Result(ToKind(output));
        if (result.Error.Length > 0)
            return UnitState.Error;
        if (result.Text is null)
            return UnitState.Pending;
        return string.IsNullOrWhiteSpace(result.Text) ? UnitState.Empty : UnitState.Ready;
    }

    /// <summary>What the user said or wrote, in order: the units' original text and then the live hypothesis.</summary>
    public string SourceText() =>
        TextUnits.Join(_units.Select(unit => (unit.Lead, unit.Raw, unit.Trail)).Append(("", Pending, "")));

    public string Text() =>
        TextUnits.Join(_units.Select(unit => (unit.Lead, BodyOf(unit, Output), unit.Trail)));

    /// <summary>Content readiness is shared by copy, export and insertion; insertion additionally needs a live target.</summary>
    public Readiness ContentReadiness()
    {
        if (_units.Count == 0)
            return Readiness.Blocked(BlockReason.Empty);

        if (Output != OutputKind.Raw)
        {
            var states = _units.Select(unit => StateOf(unit, Output)).ToList();
            if (states.Any(state => state is UnitState.Error or UnitState.Empty))
                return Readiness.Blocked(BlockReason.Unavailable);
            if (states.Contains(UnitState.Pending))
                return Readiness.Blocked(BlockReason.Processing);
        }

        return string.IsNullOrWhiteSpace(Text()) ? Readiness.Blocked(BlockReason.Empty) : Readiness.Ready;
    }

    public Readiness InsertionReadiness()
    {
        if (TargetLost)
            return Readiness.Blocked(BlockReason.TargetLost);
        if (Recording)
            return Readiness.Blocked(BlockReason.Recording);
        return ContentReadiness();
    }

    /// <summary>Freezes the draft and returns the text to write; repeated calls return the same snapshot.</summary>
    public CommitResult RequestCommit()
    {
        if (Snapshot is not null)
            return Snapshot;

        var readiness = InsertionReadiness();
        if (!readiness.IsReady)
            return CommitResult.Block(readiness.Reason!.Value);

        Snapshot = CommitResult.Success(new CommitSnapshot(Text(), HasTarget ? CommitAction.Insert : CommitAction.Copy));
        return Snapshot;
    }

    private DraftUnit NewUnit(SourceUnit source) => new(++_nextUnitId, source);

    private JobTicket TicketFor(DraftUnit unit) => new(_epoch, unit.Id, _translationRevision);

    private bool TryResolve(JobTicket ticket, UnitKind kind, out DraftUnit unit)
    {
        unit = null!;
        if (Snapshot is not null || ticket.Epoch != _epoch)
            return false;
        if (kind == UnitKind.Translation && ticket.TranslationRevision != _translationRevision)
            return false;

        var found = _units.Find(candidate => candidate.Id == ticket.UnitId);
        if (found is null || found.Result(kind).IsOwned)
            return false;

        unit = found;
        return true;
    }

    private void EnsureEditable()
    {
        if (Snapshot is not null)
            throw new InvalidOperationException("Submitted text is frozen; start a new draft first.");
    }

    private static string BodyOf(DraftUnit unit, OutputKind output) => output switch
    {
        OutputKind.Raw => unit.Raw,
        _ => unit.Result(ToKind(output)).Text ?? ""
    };

    private static UnitKind ToKind(OutputKind output) => output == OutputKind.Clean ? UnitKind.Clean : UnitKind.Translation;
}
