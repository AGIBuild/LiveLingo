using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerDocumentTests
{
    [Fact]
    public async Task FinalText_IsSharedByPreviewCopyExportAndInsertion()
    {
        await using var h = new Harness(documentMode: true);
        h.Vm.SourceText = "先检查缓存。然后更新索引。";
        await Wait.Until(() => h.Vm.CanWrite);

        Assert.Equal("T:先检查缓存。然后更新索引。", h.Vm.FinalText);
        Assert.Equal(h.Vm.FinalText, h.Vm.BuildExport(ExportFormat.Txt));
        await h.Vm.CopyCommand.ExecuteAsync(null);
        await h.Clipboard.Received(1).SetTextAsync(h.Vm.FinalText, Arg.Any<CancellationToken>());
        await h.Vm.CommitAsync(fromKey: false);
        await h.Injector.Received(1).InjectAsync(Harness.Target, h.Vm.FinalText, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullEditor_SaveDoesNotWrite_AndModeChangeSavesToTheBoundResult()
    {
        await using var h = new Harness(documentMode: true);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);
        h.Vm.EditDocumentCommand.Execute(null);
        h.Vm.EditText = "Check the cache.\nThen continue.";

        Assert.True(h.Vm.IsEditingDocument);
        Assert.False(h.Vm.CanCopy);
        await h.Vm.CommitAsync(fromKey: true);

        Assert.False(h.Vm.IsEditing);
        Assert.Equal("Check the cache.\nThen continue.", h.Vm.FinalText);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default, TestContext.Current.CancellationToken);
        h.Vm.EditDocumentCommand.Execute(null);
        h.Vm.EditText = "Manually edited translation.";
        h.Vm.ChooseOutput(ComposerOutput.Original);
        Assert.Equal("Manually edited translation.", h.Session.Read(d => d.Final(UnitKind.Translation).Text));
        Assert.True(h.Session.Read(d => d.Final(UnitKind.Translation).IsOwned));
        Assert.Equal("先检查缓存。", h.Session.Read(d => d.Text()));
    }

    [Fact]
    public async Task ClosingAndRestoring_RetainsManuallyEditedFinalTextAndChoice()
    {
        await using var h = new Harness(documentMode: true);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);
        h.Vm.EditDocumentCommand.Execute(null);
        h.Vm.EditText = "My final wording.";
        var recent = h.Vm.CaptureRecentDraft(written: false);

        await using var restored = new Harness(documentMode: true);
        restored.Vm.RestoreRecentDraft(recent);

        Assert.Equal(ComposerOutput.Translate, restored.Vm.Output);
        Assert.Equal(h.Vm.TargetLanguage, restored.Vm.TargetLanguage);
        Assert.Equal("My final wording.", restored.Vm.FinalText);
        Assert.True(restored.Vm.FinalIsOwned);
        Assert.True(restored.Vm.CanWrite);
    }

    [Fact]
    public async Task FinalFailure_CancelsQueuedWrite_UntilAnExplicitRepair()
    {
        await using var h = new Harness(documentMode: true);
        var fail = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => fail.Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。然后更新索引。";
        await h.Vm.CommitAsync(fromKey: false);
        Assert.True(h.Vm.IsWriteQueued);
        fail.SetException(new InvalidDataException("incomplete response"));
        await Wait.Until(() => h.Vm.NeedsFinalChoice);

        Assert.False(h.Vm.IsWriteQueued);
        Assert.False(h.Vm.CanCopy);
        Assert.False(h.Vm.CanWrite);
        Assert.Empty(h.Events);
        h.Vm.EditDocumentCommand.Execute(null);
        h.Vm.EditText = "Check the cache, then update the index.";
        h.Vm.SaveEditCommand.Execute(null);
        Assert.True(h.Vm.CanWrite);
        Assert.True(h.Vm.FinalIsOwned);
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task SourceChange_PreservesManualFinalButRequiresReconfirmation()
    {
        await using var h = new Harness(documentMode: true);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);
        h.Vm.EditDocumentCommand.Execute(null);
        h.Vm.EditText = "Do not replace my wording.";
        h.Vm.SaveEditCommand.Execute(null);
        h.Vm.SourceText += "然后更新索引。";

        Assert.Equal(DocumentState.Stale, h.Vm.FinalState);
        Assert.Equal("Do not replace my wording.", h.Vm.FinalText);
        Assert.False(h.Vm.CanWrite);
    }

    [Fact]
    public async Task FailedFinal_OnlyOffersCompletePreviews_AndAcceptanceDoesNotWrite()
    {
        await using var h = new Harness(documentMode: true);
        h.Translator.Handler = (_, _) => Task.FromException<TranslatedText>(new InvalidDataException("incomplete"));
        h.Vm.SourceText = "先检查缓存。然后更新索引。";
        await Wait.Until(() => h.Vm.NeedsFinalChoice);
        Assert.False(h.Vm.CanAcceptPreview);
        h.Session.Mutate(d =>
        {
            for (var i = 0; i < d.Units.Count; i++)
                d.Complete(d.TicketFor(i), UnitKind.Translation, "Preview:" + d.Units[i].Raw);
        });

        Assert.True(h.Vm.CanAcceptPreview);
        h.Vm.AcceptPreviewCommand.Execute(null);

        Assert.True(h.Vm.CanWrite);
        Assert.True(h.Vm.FinalIsOwned);
        Assert.Contains("Preview:", h.Vm.FinalText);
        Assert.Empty(h.Events);
    }
}
