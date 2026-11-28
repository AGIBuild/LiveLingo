using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Drafting;

namespace LiveLingo.Desktop.Composer;

public sealed partial class ComposerViewModel
{
    private DocumentEditTicket? _documentEdit;

    public bool DocumentMode => !HasTimeline && _session.Read(d => d.DocumentMode);
    public bool IsEditing => EditingIndex != -1;
    public bool IsEditingDocument => _documentEdit is not null;
    public DocumentState FinalState => _session.Read(d => d.Final(SelectedKind).State);
    public bool IsStreamingDocument => _session.Read(d =>
        d.Final(SelectedKind) is { State: DocumentState.Pending, StreamingText.Length: > 0 });
    public bool FinalIsOwned => _session.Read(d => d.Final(SelectedKind).IsOwned);
    public string FinalError => _session.Read(d => d.Final(SelectedKind).Error);
    public GateRejection? FinalWarning => _session.Read(d => d.Final(SelectedKind).Warning);
    public string FinalText => _session.Read(d =>
    {
        var final = d.Final(SelectedKind);
        return final.State == DocumentState.Pending && !string.IsNullOrEmpty(final.StreamingText)
            ? final.StreamingText
            : final.Text ?? d.PreviewText(SelectedKind);
    });
    public bool CanEditDocument =>
        DocumentMode && Output != ComposerOutput.Original && _committed is null &&
        !IsClearing && !IsListening && !IsStartingListening && !IsTranscribing &&
        !IsEditing && !string.IsNullOrWhiteSpace(SourceText);
    public bool NeedsFinalChoice =>
        DocumentMode && Output != ComposerOutput.Original &&
        FinalState is DocumentState.Failed or DocumentState.Stale;
    public bool CanAcceptPreview => CanEditDocument && NeedsFinalChoice && _session.Read(d =>
        d.Units.Count > 0 && d.Units.All(unit =>
        {
            var result = SelectedKind == UnitKind.Clean ? unit.Clean : unit.Translation;
            return !string.IsNullOrWhiteSpace(result.Text) && result.Error.Length == 0;
        }));

    [RelayCommand]
    private void EditDocument()
    {
        if (!CanEditDocument)
            return;

        CancelQueuedWrite();
        EditText = FinalText;
        if (EditText.Length == 0 && Output == ComposerOutput.Clean)
            EditText = SourceText;
        _documentEdit = _session.Mutate(d => d.BeginDocumentEdit(SelectedKind));
        EditingIndex = -2;
    }

    [RelayCommand]
    private void RetryDocument()
    {
        if (!CanEditDocument)
            return;

        CancelQueuedWrite();
        _session.Mutate(d => d.RetryDocument(SelectedKind));
    }

    [RelayCommand]
    private void AcceptPreview()
    {
        if (!CanAcceptPreview)
            return;

        CancelQueuedWrite();
        if (!_session.Mutate(d => d.AcceptPreview(SelectedKind)))
            ToastRaised?.Invoke(ToastKind.Unavailable);
    }

    public RecentDraft CaptureRecentDraft(bool written)
    {
        if (IsEditing)
            SaveEdit();
        return new RecentDraft(_time.GetUtcNow(), SourceText, written,
            _session.Read(d => d.CaptureState()), Output, TargetLanguage, PolishMode);
    }

    public void RestoreRecentDraft(RecentDraft recent)
    {
        if (!CanReplaceDraft)
            return;
        CancelQueuedWrite();
        if (recent.State is null)
        {
            SourceText = recent.Text;
            return;
        }

        PolishMode = _presets.Any(p => p.Enabled && string.Equals(p.Id, recent.PolishMode, StringComparison.Ordinal))
            ? recent.PolishMode
            : DefaultPolishMode;
        Output = Outputs.Contains(recent.Output) ? recent.Output : ComposerOutput.Original;
        TargetLanguage = recent.Target;
        _applyingDraftText = true;
        try
        {
            _session.Mutate(d =>
            {
                d.RestoreState(recent.State);
                d.SetPolishMode(PolishMode);
            });
            SourceText = _session.Read(d => d.SourceText());
        }
        finally
        {
            _applyingDraftText = false;
        }
        ApplyDraftOutput();
        Refresh();
    }
}
