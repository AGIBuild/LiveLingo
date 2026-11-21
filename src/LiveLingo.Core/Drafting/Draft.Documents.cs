namespace LiveLingo.Core.Drafting;

public sealed partial class Draft
{
    private readonly DocumentResult _finalClean = new();
    private readonly DocumentResult _finalTranslation = new();
    private DocumentEditTicket? _documentEdit;
    private int _editGeneration;
    private bool _forceCleanRepolish;

    public bool DocumentMode { get; set; }
    public bool DocumentEditing => _documentEdit is not null;
    internal bool ForceCleanRepolish => _forceCleanRepolish;

    /// <summary>True when at least one unit or pending regeneration requires a text cleanup/polish LLM beyond native ASR polishing.</summary>
    public bool RequiresCleanerModel =>
        _forceCleanRepolish || _units.Any(u => !u.IsNativePolished && !u.Clean.IsOwned);

    /// <summary>Marks all current unowned units as natively polished by the speech recognizer after applying deterministic glossary corrections.</summary>
    public void MarkUnitsNativePolished(IReadOnlyList<Translation.GlossaryCorrection>? corrections = null)
    {
        EnsureEditable();
        foreach (var unit in _units)
        {
            if (unit.Clean.IsOwned)
                continue;
            unit.Clean.Text = Translation.Glossary.ApplyCorrections(unit.Raw, corrections);
            unit.IsNativePolished = true;
        }
    }
    public DocumentResult Final(UnitKind kind) => kind == UnitKind.Clean ? _finalClean : _finalTranslation;
    public string PreviewText(UnitKind kind) =>
        TextUnits.Join(_units.Select(unit => (unit.Lead, unit.Result(kind).Text ?? "", unit.Trail)));

    public DocumentEditTicket BeginDocumentEdit(UnitKind kind)
    {
        EnsureEditable();
        if (DocumentEditing)
            throw new InvalidOperationException("A document edit is already open.");
        _editGeneration++;
        // Invalidate work, not the displayed results. Saving does not depend on unrelated output revisions.
        _finalClean.Revision++;
        _finalTranslation.Revision++;
        var ticket = new DocumentEditTicket(kind, _epoch, SourceRevision, Final(kind).Revision);
        _documentEdit = ticket;
        return ticket;
    }

    public bool SaveDocumentEdit(DocumentEditTicket ticket, string text)
    {
        EnsureEditable();
        if (_documentEdit != ticket)
            return false;
        _documentEdit = null;
        if (ticket.Epoch != _epoch || ticket.SourceRevision != SourceRevision)
            return false;
        OwnDocument(ticket.Kind, text);
        return true;
    }

    public void CancelDocumentEdit(DocumentEditTicket ticket)
    {
        if (_documentEdit == ticket)
            _documentEdit = null;
    }

    public void RetryDocument(UnitKind kind)
    {
        EnsureEditable();
        var result = Final(kind);
        result.IsOwned = false;
        if (kind == UnitKind.Clean)
            _forceCleanRepolish = true;
        InvalidateDocument(kind);
        if (kind == UnitKind.Clean)
            InvalidateDocument(UnitKind.Translation);
    }

    public bool AcceptPreview(UnitKind kind)
    {
        EnsureEditable();
        if (DocumentEditing || _units.Count == 0 || _units.Any(unit =>
            string.IsNullOrWhiteSpace(unit.Result(kind).Text) || unit.Result(kind).Error.Length > 0))
            return false;
        OwnDocument(kind, PreviewText(kind));
        return true;
    }

    private void OwnDocument(UnitKind kind, string text)
    {
        var result = Final(kind);
        result.Revision++;
        result.Text = text;
        result.State = DocumentState.Ready;
        result.IsOwned = true;
        result.Error = "";
        result.InputRevision = SourceRevision;
        if (kind == UnitKind.Clean)
            InvalidateDocument(UnitKind.Translation);
    }

    private bool ValidFinal(UnitKind kind) =>
        Final(kind).State == DocumentState.Ready && Final(kind).InputRevision == SourceRevision;

    private void ResetDocuments()
    {
        _documentEdit = null;
        _forceCleanRepolish = false;
        _editGeneration++;
        foreach (var kind in Enum.GetValues<UnitKind>())
        {
            var result = Final(kind);
            result.Revision++;
            result.Text = null;
            result.State = DocumentState.None;
            result.IsOwned = false;
            result.Error = "";
            result.InputRevision = SourceRevision;
        }
    }

    private void SourceChanged()
    {
        InvalidateDocuments();
    }

    private void InvalidateDocuments()
    {
        InvalidateDocument(UnitKind.Clean);
        InvalidateDocument(UnitKind.Translation);
    }

    private void InvalidateDocument(UnitKind kind)
    {
        var result = Final(kind);
        result.Revision++;
        result.State = result.IsOwned ? DocumentState.Stale
            : _units.Count > 0 ? DocumentState.Pending : DocumentState.None;
        result.Error = "";
    }

    private void CleanUnitChanged(DraftUnit unit)
    {
        if (!DocumentMode)
            return; // Legacy unit clients may intentionally retain translations across cleanup edits.
        // A translation that read the old cleanup must not survive it (including a late callback).
        unit.TranslationRevision++;
        if (!unit.Translation.IsOwned)
            unit.Translation.Clear();
    }

    internal bool DocumentDue(UnitKind kind) =>
        DocumentMode && Snapshot is null && !Recording && !DocumentEditing && _units.Count > 0
        && Output == (kind == UnitKind.Clean ? OutputKind.Clean : OutputKind.Translation)
        && !Final(kind).IsOwned && Final(kind).State is DocumentState.None or DocumentState.Pending;

    internal DocumentTicket DocumentTicketFor(UnitKind kind) =>
        new(kind, _epoch, SourceRevision, Final(kind).Revision, _translationRevision, _editGeneration);

    internal bool IsCurrent(DocumentTicket ticket) =>
        DocumentDue(ticket.Kind) && ticket.Epoch == _epoch && ticket.SourceRevision == SourceRevision
        && ticket.ResultRevision == Final(ticket.Kind).Revision && ticket.EditGeneration == _editGeneration
        && (ticket.Kind != UnitKind.Translation || ticket.TranslationRevision == _translationRevision);

    internal bool CompleteDocument(DocumentTicket ticket, string? text, string error = "")
    {
        if (!IsCurrent(ticket))
            return false;
        var result = Final(ticket.Kind);
        result.Text = text ?? result.Text;
        result.InputRevision = ticket.SourceRevision;
        result.State = error.Length > 0 ? DocumentState.Failed : DocumentState.Ready;
        result.Error = error;
        result.Revision++;
        if (ticket.Kind == UnitKind.Clean && error.Length == 0)
        {
            _forceCleanRepolish = false;
            InvalidateDocument(UnitKind.Translation);
        }
        return true;
    }

    internal string? TranslationDocumentInput() =>
        ValidFinal(UnitKind.Clean) && !string.IsNullOrWhiteSpace(_finalClean.Text) ? _finalClean.Text
        : _units.Count > 0 && _units.All(u => (u.IsNativePolished || u.Clean.IsOwned) && !string.IsNullOrWhiteSpace(u.Clean.Text))
            ? PreviewText(UnitKind.Clean)
            : null;

    public DraftState CaptureState()
    {
        static ResultState Unit(UnitResult r) => new(r.Text, r.IsOwned, r.Error, r.Rejection);
        static DocumentResultState Doc(DocumentResult r) => new(r.Text, r.State, r.IsOwned, r.Error, r.InputRevision);
        return new DraftState(_units.Select(u => new DraftUnitState(
            new SourceUnit(u.Raw, u.Lead, u.Trail, u.IsComplete), Unit(u.Clean), Unit(u.Translation))).ToArray(),
            Route, Output, Pending, SourceRevision, Doc(_finalClean), Doc(_finalTranslation));
    }

    public void RestoreState(DraftState state)
    {
        Reset(HasTarget);
        Route = state.Route;
        Output = state.Output;
        EditRevision++;
        static void Unit(UnitResult to, ResultState from)
        {
            to.Text = from.Text;
            to.IsOwned = from.IsOwned;
            to.Error = from.Error;
            to.Rejection = from.Rejection;
        }
        foreach (var saved in state.Units)
        {
            var unit = NewUnit(saved.Source);
            Unit(unit.Clean, saved.Clean);
            Unit(unit.Translation, saved.Translation);
            _units.Add(unit);
        }
        void Doc(DocumentResult to, DocumentResultState from)
        {
            to.Text = from.Text;
            to.IsOwned = from.IsOwned;
            to.Error = from.Error;
            to.State = from.State;
            if (from.State == DocumentState.None && _units.Count > 0)
                to.State = DocumentState.Pending;
            to.InputRevision = from.InputRevision == state.SourceRevision ? SourceRevision : SourceRevision - 1;
        }
        Doc(_finalClean, state.Clean);
        Doc(_finalTranslation, state.Translation);
        // A live recognized tail is source, not a processing state to resurrect after reopening.
        if (state.Pending.Length > 0)
        {
            _units.Add(NewUnit(new SourceUnit(state.Pending)));
            InvalidateDocuments();
        }
    }
}
