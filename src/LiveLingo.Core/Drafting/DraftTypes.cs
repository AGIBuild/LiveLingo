namespace LiveLingo.Core.Drafting;

public enum PolishOutcome
{
    None,
    DefaultRequirements,
    KeptPreview
}

/// <summary>Which stored view of the draft is written out.</summary>
public enum OutputKind
{
    Raw,
    Clean,
    Translation
}

/// <summary>The model-produced views a unit can hold.</summary>
public enum UnitKind
{
    Clean,
    Translation
}

public enum UnitState
{
    Pending,
    Ready,
    Error,
    Empty
}

/// <summary>Whether the source is processed sentence by sentence or as one finished document.</summary>
public enum DraftRoute
{
    Units,
    Whole
}

public enum BlockReason
{
    Empty,
    Unavailable,
    Processing,
    Recording,
    TargetLost
}

public enum CommitAction
{
    Insert,
    Copy
}

/// <summary>
/// Identifies one piece of asynchronous work. A result is accepted only while the draft session,
/// the unit and (for translations) the translation revision it was issued for are still current.
/// </summary>
public readonly record struct JobTicket(int Epoch, int UnitId, int TranslationRevision);

/// <summary>A source sentence or line with the layout whitespace around it, which is never sent to a model.</summary>
public sealed record SourceUnit(string Body, string Lead = "", string Trail = "", bool IsComplete = true);

public sealed record CommitSnapshot(string Text, CommitAction Action);

public sealed record CommitResult
{
    public CommitSnapshot? Snapshot { get; private init; }
    public BlockReason? Blocked { get; private init; }
    public bool IsSuccess => Snapshot is not null;

    public static CommitResult Success(CommitSnapshot snapshot) => new() { Snapshot = snapshot };
    public static CommitResult Block(BlockReason reason) => new() { Blocked = reason };
}

public readonly record struct Readiness(bool IsReady, BlockReason? Reason = null)
{
    public static Readiness Ready { get; } = new(true);
    public static Readiness Blocked(BlockReason reason) => new(false, reason);
}
