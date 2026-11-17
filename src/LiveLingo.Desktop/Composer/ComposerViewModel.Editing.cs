using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;

namespace LiveLingo.Desktop.Composer;

/// <summary>Per-sentence actions of the result pane: edit in place, remember an accent correction, or go back to what was said.</summary>
public sealed partial class ComposerViewModel
{
    private string _editingOriginalText = "";
    private UnitKind _editingKind;
    private int _editingUnitId;

    /// <summary>The active glossary for this composer session, used to detect new accent corrections during inline edits.</summary>
    public Glossary ActiveGlossary { get; set; } = Glossary.Empty;

    /// <summary>Raised when the user saves an inline edit and chooses to remember the detected accent correction.</summary>
    public event Action<GlossaryCorrection>? CorrectionRemembered;

    /// <summary>Index of the sentence being edited, or -1.</summary>
    [ObservableProperty] private int _editingIndex = -1;

    [ObservableProperty] private string _editText = "";

    /// <summary>A concise single-term correction detected between the original row text and <see cref="EditText"/>.</summary>
    public GlossaryCorrection? SuggestedCorrection =>
        EditingIndex >= 0 && Output != ComposerOutput.Translate
            ? ActiveGlossary.SuggestCorrection(_editingOriginalText, EditText)
            : null;

    /// <summary>Sentence tools show on hover only while nothing is recording, written or being edited.</summary>
    public bool CanEditRows => _committed is null && !IsClearing && !IsListening && !IsStartingListening && !IsTranscribing && !IsEditing;

    partial void OnEditingIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CanEditRows));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsEditingDocument));
        OnPropertyChanged(nameof(CanEditDocument));
        OnPropertyChanged(nameof(SuggestedCorrection));
        UpdateEditable();
        Refresh();
    }

    partial void OnEditTextChanged(string value) => OnPropertyChanged(nameof(SuggestedCorrection));

    private UnitKind SelectedKind => Output == ComposerOutput.Translate ? UnitKind.Translation : UnitKind.Clean;

    [RelayCommand]
    private void StartEdit(int index)
    {
        if (!CanEditRows || Output == ComposerOutput.Original || index < 0 || index >= Lines.Count)
            return;

        CancelQueuedWrite();
        _editingOriginalText = Lines[index].Text;
        EditText = Lines[index].Text;
        _editingKind = SelectedKind;
        _editingUnitId = _session.Read(d => d.Units[index].Id);
        EditingIndex = index;
    }

    /// <summary>Stores the edited text as the user's own; an empty text is kept as an empty sentence.</summary>
    [RelayCommand]
    private void SaveEdit()
    {
        if (!IsEditing)
            return;

        if (_documentEdit is { } ticket)
        {
            var saved = _session.Mutate(d => d.SaveDocumentEdit(ticket, EditText));
            if (!saved)
            {
                ToastRaised?.Invoke(ToastKind.EditExpired);
                return;
            }
            _documentEdit = null;
            EditingIndex = -1;
            return;
        }

        var index = EditingIndex;
        var text = EditText;
        var kind = _editingKind;
        _editingOriginalText = "";
        EditingIndex = -1;
        var savedRow = _session.Mutate(d =>
        {
            if (index >= d.Units.Count || d.Units[index].Id != _editingUnitId)
                return false;
            d.Edit(index, kind, text);
            return true;
        });
        if (!savedRow)
            ToastRaised?.Invoke(ToastKind.EditExpired);
    }

    /// <summary>Saves the edited sentence and records the detected misrecognition rule in the user's glossary.</summary>
    [RelayCommand]
    private void SaveEditAndRemember()
    {
        if (EditingIndex < 0)
            return;

        var correction = SuggestedCorrection;
        SaveEdit();
        if (correction is null)
            return;

        var existingLines = ActiveGlossary.Corrections
            .Select(c => $"{c.Spoken}=>{c.Canonical}")
            .Append($"{correction.Spoken}=>{correction.Canonical}");
        ActiveGlossary = Glossary.Parse(string.Join('\n', ActiveGlossary.Terms.Concat(existingLines)));
        CorrectionRemembered?.Invoke(correction);
        ToastRaised?.Invoke(ToastKind.CorrectionSaved);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (_documentEdit is { } ticket)
            _session.Mutate(d => d.CancelDocumentEdit(ticket));
        _documentEdit = null;
        _editingOriginalText = "";
        EditingIndex = -1;
    }

    /// <summary>Replaces the cleaned sentence with what was said.</summary>
    [RelayCommand]
    private void UseOriginal(int index)
    {
        if (!CanEditRows || Output != ComposerOutput.Clean)
            return;

        CancelQueuedWrite();
        _session.Mutate(draft => draft.UseRaw(index));
    }
}
