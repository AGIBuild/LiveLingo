using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Desktop.Composer;

/// <summary>Per-sentence actions of the result pane: edit in place, or go back to what was said.</summary>
public sealed partial class ComposerViewModel
{
    /// <summary>Index of the sentence being edited, or -1.</summary>
    [ObservableProperty] private int _editingIndex = -1;

    [ObservableProperty] private string _editText = "";

    /// <summary>Sentence tools show on hover only while nothing is recording, written or being edited.</summary>
    public bool CanEditRows => _committed is null && !IsListening && !IsStartingListening && !IsTranscribing && EditingIndex < 0;

    partial void OnEditingIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CanEditRows));
        UpdateEditable();
        Refresh();
    }

    private UnitKind SelectedKind => Output == ComposerOutput.Translate ? UnitKind.Translation : UnitKind.Clean;

    [RelayCommand]
    private void StartEdit(int index)
    {
        if (!CanEditRows || Output == ComposerOutput.Original || index < 0 || index >= Lines.Count)
            return;

        CancelQueuedWrite();
        EditText = Lines[index].Text;
        EditingIndex = index;
    }

    /// <summary>Stores the edited text as the user's own; an empty text is kept as an empty sentence.</summary>
    [RelayCommand]
    private void SaveEdit()
    {
        if (EditingIndex < 0)
            return;

        var index = EditingIndex;
        var text = EditText;
        var kind = SelectedKind;
        EditingIndex = -1;
        _session.Mutate(draft => draft.Edit(index, kind, text));
    }

    [RelayCommand]
    private void CancelEdit() => EditingIndex = -1;

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
