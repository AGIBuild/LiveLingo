namespace LiveLingo.Core.Drafting;

public enum DocumentState { None, Pending, Ready, Failed, Stale }

public sealed class DocumentResult
{
    public string? Text { get; internal set; }
    public DocumentState State { get; internal set; }
    public bool IsOwned { get; internal set; }
    public string Error { get; internal set; } = "";
    public int InputRevision { get; internal set; }
    internal int Revision { get; set; }
}

public readonly record struct DocumentEditTicket(UnitKind Kind, int Epoch, int SourceRevision, int ResultRevision);

internal readonly record struct DocumentTicket(
    UnitKind Kind, int Epoch, int SourceRevision, int ResultRevision, int TranslationRevision, int EditGeneration);

// Detached values only: no running jobs, edit locks, insertion targets or committed snapshots.
public sealed record ResultState(string? Text, bool IsOwned, string Error, GateRejection? Rejection = null);
public sealed record DraftUnitState(SourceUnit Source, ResultState Clean, ResultState Translation);
public sealed record DocumentResultState(string? Text, DocumentState State, bool IsOwned, string Error, int InputRevision);
public sealed record DraftState(
    IReadOnlyList<DraftUnitState> Units, DraftRoute Route, OutputKind Output, string Pending, int SourceRevision,
    DocumentResultState Clean, DocumentResultState Translation);
