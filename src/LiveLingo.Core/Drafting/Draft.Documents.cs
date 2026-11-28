using LiveLingo.Core.Cleanup;

namespace LiveLingo.Core.Drafting;

public sealed partial class Draft
{
    private readonly Dictionary<string, DocumentResult> _cleanDocs = new(StringComparer.Ordinal);
    private readonly DocumentResult _finalTranslation = new();
    private DocumentEditTicket? _documentEdit;
    private int _editGeneration;
    private bool _forceCleanRepolish;

    public bool DocumentMode { get; set; }
    public string PolishModeId { get; private set; } = PolishPresets.NaturalId;
    public int ModeSwitchRevision { get; private set; }
    public bool DocumentEditing => _documentEdit is not null;
    internal bool ForceCleanRepolish => _forceCleanRepolish;

    /// <summary>True when at least one unit, active structured polish mode, or pending regeneration requires a text LLM beyond native ASR polishing.</summary>
    public bool RequiresCleanerModel =>
        _forceCleanRepolish
        || !string.Equals(PolishModeId, PolishPresets.NaturalId, StringComparison.Ordinal)
        || _units.Any(u => !u.IsNativePolished && !u.Clean.IsOwned);

    private DocumentResult CleanFor(string modeId)
    {
        var key = string.IsNullOrWhiteSpace(modeId) ? PolishPresets.NaturalId : modeId;
        if (!_cleanDocs.TryGetValue(key, out var doc))
        {
            doc = new DocumentResult
            {
                State = _units.Count > 0 ? DocumentState.Pending : DocumentState.None,
                InputRevision = SourceRevision
            };
            _cleanDocs[key] = doc;
        }
        return doc;
    }

    /// <summary>Selects the scenario polish mode for <see cref="OutputKind.Clean"/> while keeping finished or edited drafts of other modes cached.</summary>
    public void SetPolishMode(string modeId)
    {
        EnsureEditable();
        var normalized = string.IsNullOrWhiteSpace(modeId) ? PolishPresets.NaturalId : modeId;
        if (string.Equals(PolishModeId, normalized, StringComparison.Ordinal))
            return;

        if (_documentEdit is { Kind: UnitKind.Clean })
        {
            _documentEdit = null;
            _editGeneration++;
        }

        PolishModeId = normalized;
        ModeSwitchRevision++;
        var doc = CleanFor(normalized);
        if (!doc.IsOwned && doc.State is DocumentState.None or DocumentState.Failed && _units.Count > 0)
        {
            doc.Revision++;
            doc.Error = "";
            doc.StreamingText = null;
            doc.State = DocumentState.Pending;
        }
    }

    /// <summary>Invalidates an unowned cached document for <paramref name="modeId"/> when its preset instructions changed in Settings.</summary>
    public void InvalidatePolishMode(string modeId)
    {
        EnsureEditable();
        if (!_cleanDocs.TryGetValue(modeId, out var result))
            return;

        result.Revision++;
        result.StreamingText = null;
        result.State = result.IsOwned ? DocumentState.Stale
            : _units.Count > 0 ? DocumentState.Pending : DocumentState.None;
        result.Error = "";
    }

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

    public DocumentResult Final(UnitKind kind) => kind == UnitKind.Clean ? CleanFor(PolishModeId) : _finalTranslation;

    public string PreviewText(UnitKind kind) =>
        TextUnits.Join(_units.Select(unit => (
            unit.Lead,
            kind == UnitKind.Clean ? (unit.Clean.Text ?? unit.Raw) : (unit.Translation.Text ?? ""),
            unit.Trail)));

    public DocumentEditTicket BeginDocumentEdit(UnitKind kind)
    {
        EnsureEditable();
        if (DocumentEditing)
            throw new InvalidOperationException("A document edit is already open.");
        _editGeneration++;
        // Invalidate work, not the displayed results. Saving does not depend on unrelated output revisions.
        foreach (var clean in _cleanDocs.Values)
        {
            clean.Revision++;
            clean.StreamingText = null;
        }
        _finalTranslation.Revision++;
        _finalTranslation.StreamingText = null;
        var modeId = kind == UnitKind.Clean ? PolishModeId : PolishPresets.NaturalId;
        var ticket = new DocumentEditTicket(kind, _epoch, SourceRevision, Final(kind).Revision, modeId);
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
        OwnDocument(ticket.Kind, text, ticket.PolishModeId);
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
        ModeSwitchRevision++;
        var result = Final(kind);
        result.IsOwned = false;
        if (kind == UnitKind.Clean)
        {
            _forceCleanRepolish = true;
            InvalidateSingleDocument(result);
            if (string.Equals(PolishModeId, PolishPresets.NaturalId, StringComparison.Ordinal))
                InvalidateDocument(UnitKind.Translation);
            return;
        }
        InvalidateDocument(UnitKind.Translation);
    }

    public bool AcceptPreview(UnitKind kind)
    {
        EnsureEditable();
        if (DocumentEditing || _units.Count == 0 || _units.Any(unit =>
            string.IsNullOrWhiteSpace(unit.Result(kind).Text) || unit.Result(kind).Error.Length > 0))
            return false;
        OwnDocument(kind, PreviewText(kind), PolishModeId);
        return true;
    }

    private void OwnDocument(UnitKind kind, string text, string modeId)
    {
        var result = kind == UnitKind.Clean ? CleanFor(modeId) : _finalTranslation;
        result.Revision++;
        result.Text = text;
        result.StreamingText = null;
        result.State = DocumentState.Ready;
        result.IsOwned = true;
        result.Error = "";
        result.Warning = null;
        result.InputRevision = SourceRevision;
        if (kind == UnitKind.Clean && string.Equals(modeId, PolishPresets.NaturalId, StringComparison.Ordinal))
            InvalidateDocument(UnitKind.Translation);
    }

    private bool ValidFinal(UnitKind kind) =>
        Final(kind).State == DocumentState.Ready && Final(kind).InputRevision == SourceRevision;

    private bool ValidNaturalFinal() =>
        _cleanDocs.TryGetValue(PolishPresets.NaturalId, out var natural)
        && natural.State == DocumentState.Ready
        && natural.InputRevision == SourceRevision
        && !string.IsNullOrWhiteSpace(natural.Text);

    private void ResetDocuments()
    {
        _documentEdit = null;
        _forceCleanRepolish = false;
        _editGeneration++;
        PolishModeId = PolishPresets.NaturalId;
        _cleanDocs.Clear();
        _cleanDocs[PolishPresets.NaturalId] = new DocumentResult { InputRevision = SourceRevision };
        _finalTranslation.Revision++;
        _finalTranslation.Text = null;
        _finalTranslation.StreamingText = null;
        _finalTranslation.State = DocumentState.None;
        _finalTranslation.IsOwned = false;
        _finalTranslation.Error = "";
        _finalTranslation.InputRevision = SourceRevision;
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
        if (kind == UnitKind.Clean)
        {
            _ = CleanFor(PolishModeId);
            foreach (var doc in _cleanDocs.Values)
                InvalidateSingleDocument(doc);
            return;
        }
        InvalidateSingleDocument(_finalTranslation);
    }

    private void InvalidateSingleDocument(DocumentResult result)
    {
        result.Revision++;
        result.StreamingText = null;
        result.State = result.IsOwned ? DocumentState.Stale
            : _units.Count > 0 ? DocumentState.Pending : DocumentState.None;
        if (!result.IsOwned)
        {
            result.Text = null;
            result.Warning = null;
        }
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
        new(kind, _epoch, SourceRevision, Final(kind).Revision, _translationRevision, _editGeneration,
            kind == UnitKind.Clean ? PolishModeId : PolishPresets.NaturalId);

    internal bool IsCurrent(DocumentTicket ticket) =>
        DocumentDue(ticket.Kind) && ticket.Epoch == _epoch && ticket.SourceRevision == SourceRevision
        && (ticket.Kind != UnitKind.Clean || string.Equals(ticket.PolishModeId, PolishModeId, StringComparison.Ordinal))
        && ticket.ResultRevision == Final(ticket.Kind).Revision && ticket.EditGeneration == _editGeneration
        && (ticket.Kind != UnitKind.Translation || ticket.TranslationRevision == _translationRevision);

    internal bool ClearStreamingDocument(DocumentTicket ticket)
    {
        if (!IsCurrent(ticket))
            return false;
        var result = ticket.Kind == UnitKind.Clean ? CleanFor(ticket.PolishModeId) : _finalTranslation;
        if (result.State != DocumentState.Pending || result.StreamingText is null)
            return false;
        result.StreamingText = null;
        return true;
    }

    internal bool StreamDocument(DocumentTicket ticket, string partial)
    {
        if (!IsCurrent(ticket) || string.IsNullOrEmpty(partial))
            return false;
        var result = ticket.Kind == UnitKind.Clean ? CleanFor(ticket.PolishModeId) : _finalTranslation;
        if (result.State != DocumentState.Pending || string.Equals(result.StreamingText, partial, StringComparison.Ordinal))
            return false;
        result.StreamingText = partial;
        return true;
    }

    internal bool CompleteDocument(DocumentTicket ticket, string? text, string error = "", GateRejection? warning = null)
    {
        if (!IsCurrent(ticket))
            return false;
        var result = ticket.Kind == UnitKind.Clean ? CleanFor(ticket.PolishModeId) : _finalTranslation;
        result.StreamingText = null;
        result.Text = text ?? result.Text;
        result.InputRevision = ticket.SourceRevision;
        result.State = error.Length > 0 ? DocumentState.Failed : DocumentState.Ready;
        result.Error = error;
        result.Warning = error.Length == 0 ? warning : null;
        result.Revision++;
        if (ticket.Kind == UnitKind.Clean && error.Length == 0)
        {
            _forceCleanRepolish = false;
            if (string.Equals(ticket.PolishModeId, PolishPresets.NaturalId, StringComparison.Ordinal))
                InvalidateDocument(UnitKind.Translation);
        }
        return true;
    }

    /// <summary>
    /// Translation only consumes verbatim <c>natural</c> cleanup (or natively polished ASR units), never a structured
    /// AI prompt, task list or status report.
    /// </summary>
    internal string? TranslationDocumentInput() =>
        ValidNaturalFinal() ? _cleanDocs[PolishPresets.NaturalId].Text
        : _units.Count > 0 && _units.All(u => (u.IsNativePolished || u.Clean.IsOwned) && !string.IsNullOrWhiteSpace(u.Clean.Text))
            ? PreviewText(UnitKind.Clean)
            : null;

    public DraftState CaptureState()
    {
        static ResultState Unit(UnitResult r) => new(r.Text, r.IsOwned, r.Error, r.Rejection);
        static DocumentResultState Doc(DocumentResult r) => new(r.Text, r.State, r.IsOwned, r.Error, r.InputRevision, r.Warning);
        var activeClean = CleanFor(PolishModeId);
        var byMode = _cleanDocs.ToDictionary(kv => kv.Key, kv => Doc(kv.Value), StringComparer.Ordinal);
        return new DraftState(
            _units.Select(u => new DraftUnitState(
                new SourceUnit(u.Raw, u.Lead, u.Trail, u.IsComplete), Unit(u.Clean), Unit(u.Translation), u.IsDictated)).ToArray(),
            Route, Output, Pending, SourceRevision, Doc(activeClean), Doc(_finalTranslation), PolishModeId, byMode);
    }

    public void RestoreState(DraftState state)
    {
        Reset(HasTarget);
        Route = state.Route;
        Output = state.Output;
        PolishModeId = string.IsNullOrWhiteSpace(state.PolishModeId) ? PolishPresets.NaturalId : state.PolishModeId;
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
            unit.IsDictated = saved.IsDictated;
            Unit(unit.Clean, saved.Clean);
            Unit(unit.Translation, saved.Translation);
            _units.Add(unit);
        }
        void Doc(DocumentResult to, DocumentResultState from)
        {
            to.Text = from.Text;
            to.IsOwned = from.IsOwned;
            to.Error = from.Error;
            to.Warning = from.Warning;
            to.State = from.State;
            if (from.State == DocumentState.None && _units.Count > 0)
                to.State = DocumentState.Pending;
            to.InputRevision = from.InputRevision == state.SourceRevision ? SourceRevision : SourceRevision - 1;
        }
        _cleanDocs.Clear();
        if (state.CleanByMode is { Count: > 0 })
        {
            foreach (var (modeId, savedDoc) in state.CleanByMode)
            {
                var doc = new DocumentResult();
                Doc(doc, savedDoc);
                _cleanDocs[modeId] = doc;
            }
        }
        else
        {
            var doc = new DocumentResult();
            Doc(doc, state.Clean);
            _cleanDocs[PolishModeId] = doc;
        }
        _ = CleanFor(PolishModeId);
        Doc(_finalTranslation, state.Translation);
        // A live recognized tail is source, not a processing state to resurrect after reopening.
        if (state.Pending.Length > 0)
        {
            _units.Add(NewUnit(new SourceUnit(state.Pending)));
            InvalidateDocuments();
        }
    }
}
