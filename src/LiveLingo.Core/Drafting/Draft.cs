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

    /// <summary>The frozen result of a successful commit; the draft accepts no further changes once set.</summary>
    public CommitResult? Snapshot { get; private set; }

    public void Reset(bool hasTarget)
    {
        _epoch++;
        _translationRevision = 0;
        _units.Clear();
        Output = OutputKind.Clean;
        Route = DraftRoute.Units;
        HasTarget = hasTarget;
        TargetLost = false;
        Recording = false;
        Snapshot = null;
    }

    public JobTicket Append(string raw)
    {
        EnsureEditable();
        var unit = NewUnit(new SourceUnit(raw));
        _units.Add(unit);
        return TicketFor(unit);
    }

    public JobTicket TicketFor(int index) => TicketFor(_units[index]);

    /// <summary>
    /// Reconciles the units with the units of the source text. Units whose body is unchanged keep their results
    /// and manual edits; only new units return tickets for processing.
    /// </summary>
    public IReadOnlyList<JobTicket> SyncSource(IReadOnlyList<SourceUnit> source, DraftRoute route)
    {
        EnsureEditable();
        var pool = new Dictionary<string, Queue<DraftUnit>>();
        foreach (var unit in _units)
        {
            if (!pool.TryGetValue(unit.Raw, out var queue))
                pool[unit.Raw] = queue = new Queue<DraftUnit>();
            queue.Enqueue(unit);
        }

        var created = new List<JobTicket>();
        var next = new List<DraftUnit>(source.Count);
        foreach (var item in source)
        {
            if (pool.TryGetValue(item.Body, out var queue) && queue.TryDequeue(out var reused))
            {
                reused.Apply(item);
                next.Add(reused);
                continue;
            }

            var unit = NewUnit(item);
            created.Add(TicketFor(unit));
            next.Add(unit);
        }

        _units.Clear();
        _units.AddRange(next);
        Route = route;
        return created;
    }

    /// <summary>Stores a model result. Returns false when the ticket is stale or the user owns the text.</summary>
    public bool Complete(JobTicket ticket, UnitKind kind, string text, string warning = "")
    {
        if (!TryResolve(ticket, kind, out var unit))
            return false;

        var result = unit.Result(kind);
        result.Text = warning.Length > 0 ? unit.Raw : text;
        result.Error = "";
        result.Warning = warning;
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
