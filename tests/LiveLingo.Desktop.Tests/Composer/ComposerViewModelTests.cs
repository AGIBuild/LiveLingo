using LiveLingo.Core;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerViewModelTests
{
    [Fact]
    public async Task Typing_ProducesTranslatedLines()
    {
        await using var h = new Harness();

        h.Vm.SourceText = "先检查缓存。";

        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        var line = Assert.Single(h.Vm.Lines);
        Assert.Equal("T:先检查缓存。", line.Text);
        Assert.Equal(LineState.Ready, line.State);
        Assert.True(h.Vm.CanWrite);
        Assert.True(h.Vm.CanCopy);
    }

    [Fact]
    public async Task EmptySource_HasNothingToWrite()
    {
        await using var h = new Harness();

        Assert.Equal(ComposerStatus.Empty, h.Vm.Status);
        Assert.False(h.Vm.CanWrite);
        await h.Vm.WriteCommand.ExecuteAsync(null);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);
    }

    [Fact]
    public async Task Write_InjectsTheTranslation_WithoutAutoSend_AndCompletes()
    {
        await using var h = new Harness();
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        await h.Injector.Received(1).InjectAsync(Harness.Target, "T:先检查缓存。", false, Arg.Any<CancellationToken>());
        Assert.Equal(["completed"], h.Events);
        Assert.False(h.Vm.IsEditable);
    }

    [Fact]
    public async Task Write_WhileTranslating_IsQueuedAndRunsWhenReady()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。";

        await h.Vm.WriteCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsWriteQueued);
        await h.Vm.WriteCommand.ExecuteAsync(null);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);

        gate.SetResult(new TranslatedText("Check the cache.", 1, 2));

        await Wait.Until(() => h.Events.Contains("completed"));
        await h.Injector.Received(1).InjectAsync(Harness.Target, "Check the cache.", false, Arg.Any<CancellationToken>());
        Assert.False(h.Vm.IsWriteQueued);
    }

    [Fact]
    public async Task Write_TranslatesTheUnfinishedTailImmediately()
    {
        await using var h = new Harness();
        h.Vm.SourceText = "先检查缓存。然后更新";

        await h.Vm.WriteCommand.ExecuteAsync(null);

        await Wait.Until(() => h.Events.Contains("completed"));
        await h.Injector.Received(1).InjectAsync(Harness.Target, "T:先检查缓存。T:然后更新", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueuedWrite_IsCancelledByEditing_ChangingOutput_OrEscape()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (_, ct) => gate.Task.WaitAsync(ct);
        h.Vm.SourceText = "先检查缓存。";

        await h.Vm.WriteCommand.ExecuteAsync(null);
        h.Vm.SourceText = "先检查缓存。再更新。";
        Assert.False(h.Vm.IsWriteQueued);

        await h.Vm.WriteCommand.ExecuteAsync(null);
        h.Vm.Output = ComposerOutput.Translate;
        h.Vm.Output = ComposerOutput.Original;
        Assert.False(h.Vm.IsWriteQueued);

        h.Vm.Output = ComposerOutput.Translate;
        await h.Vm.WriteCommand.ExecuteAsync(null);
        h.Vm.CancelCommand.Execute(null);
        Assert.False(h.Vm.IsWriteQueued);
        Assert.Empty(h.Events);

        h.Vm.CancelCommand.Execute(null);
        Assert.Equal(["close"], h.Events);
    }

    [Fact]
    public async Task QueuedWrite_IsCancelledWhenTheTranslationFails()
    {
        await using var h = new Harness();
        h.Translator.Handler = (_, _) => Task.FromException<TranslatedText>(new HttpRequestException("refused"));
        h.Vm.SourceText = "先检查缓存。";

        await h.Vm.WriteCommand.ExecuteAsync(null);
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Failed);

        Assert.False(h.Vm.IsWriteQueued);
        Assert.Equal(LineState.Failed, h.Vm.Lines[0].State);
        Assert.Equal("refused", h.Vm.Lines[0].Error);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);
    }

    [Fact]
    public async Task Retry_TranslatesAFailedLineAgain()
    {
        await using var h = new Harness();
        var calls = 0;
        h.Translator.Handler = (request, _) => ++calls == 1
            ? Task.FromException<TranslatedText>(new HttpRequestException("boom"))
            : Task.FromResult(new TranslatedText("T:" + request.Source, 1, 2));
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Failed);

        h.Vm.RetryCommand.Execute(0);

        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Equal("T:先检查缓存。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task OriginalOutput_WritesTheSourceWithoutTranslating()
    {
        await using var h = new Harness();
        h.Vm.Output = ComposerOutput.Original;
        h.Vm.SourceText = "先检查缓存。\n  再更新索引。";
        await Wait.Until(() => h.Vm.CanWrite);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        await h.Injector.Received(1).InjectAsync(Harness.Target, "先检查缓存。\n  再更新索引。", false, Arg.Any<CancellationToken>());
        Assert.Empty(h.Translator.Requests);
    }

    [Fact]
    public async Task LostTarget_BlocksWriting_ButCopyStillWorks()
    {
        await using var h = new Harness();
        h.Tracker.IsAlive(Arg.Any<TargetWindowInfo>()).Returns(false);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        Assert.Equal(BannerKind.TargetLost, h.Vm.Banner.Kind);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);
        Assert.Empty(h.Events);

        await h.Vm.CopyCommand.ExecuteAsync(null);
        await h.Clipboard.Received(1).SetTextAsync("T:先检查缓存。", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutATarget_WriteCopiesToTheClipboard()
    {
        await using var h = new Harness(hasTarget: false);
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        await h.Clipboard.Received(1).SetTextAsync("T:先检查缓存。", Arg.Any<CancellationToken>());
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);
        Assert.Equal(["completed"], h.Events);
        Assert.False(h.Vm.HasTarget);
    }

    [Fact]
    public async Task InjectionFailure_ShowsABanner_KeepsTheResultCopyable_AndDoesNotComplete()
    {
        await using var h = new Harness();
        h.Injector.InjectAsync(Arg.Any<TargetWindowInfo>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no focus")));
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.CanWrite);

        await h.Vm.WriteCommand.ExecuteAsync(null);

        Assert.Equal(BannerKind.WriteFailed, h.Vm.Banner.Kind);
        Assert.Equal("no focus", h.Vm.Banner.Detail);
        Assert.Empty(h.Events);
        Assert.True(h.Vm.CanCopy);
        Assert.False(h.Vm.CanWrite);

        await h.Vm.CopyCommand.ExecuteAsync(null);
        await h.Clipboard.Received(1).SetTextAsync("T:先检查缓存。", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwapLanguage_RetranslatesIntoTheOtherLanguage()
    {
        await using var h = new Harness();
        h.Vm.SourceText = "先检查缓存。";
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        h.Vm.SwapLanguageCommand.Execute(null);

        Assert.Equal(TargetLanguage.Chinese, h.Vm.TargetLanguage);
        await Wait.Until(() => h.Translator.Requests.Count == 2);
        Assert.Equal(TargetLanguage.Chinese, h.Translator.Requests[1].Target);

        h.Vm.SwapLanguageCommand.Execute(null);
        Assert.Equal(TargetLanguage.English, h.Vm.TargetLanguage);
    }

    [Fact]
    public async Task MissingModels_AskForConsent_AndNothingIsTranslatedOrWritten()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Vm.SourceText = "先检查缓存。";
        await Task.Delay(50);

        Assert.Equal(BannerKind.ModelMissing, h.Vm.Banner.Kind);
        Assert.False(h.Vm.ModelsReady);
        Assert.Equal(ComposerStatus.Working, h.Vm.Status);
        Assert.False(h.Vm.CanWrite);
        Assert.False(h.Vm.CanCopy);
        Assert.Equal(LineState.Pending, h.Vm.Lines[0].State);
        Assert.Empty(h.Translator.Requests);

        await h.Vm.WriteCommand.ExecuteAsync(null);
        await h.Vm.CopyCommand.ExecuteAsync(null);
        await h.Injector.DidNotReceiveWithAnyArgs().InjectAsync(default!, default!, default);
        await h.Clipboard.DidNotReceiveWithAnyArgs().SetTextAsync(default!, default);
    }

    [Fact]
    public async Task Download_InstallsMissingModels_ThenTranslates()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(call =>
        {
            h.Models.HasAllExpectedLocalAssets(call.Arg<ModelDescriptor>()).Returns(true);
            return Task.CompletedTask;
        });
        h.Vm.SourceText = "先检查缓存。";

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        await h.Downloads.Received(1).StartAsync(h.Runtime);
        await h.Downloads.Received(1).StartAsync(h.TranslatorModel);
        Assert.True(h.Vm.ModelsReady);
        Assert.Equal(BannerKind.None, h.Vm.Banner.Kind);
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Equal("T:先检查缓存。", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task Download_ShowsProgress_AndReportsFailures()
    {
        await using var h = new Harness(modelsInstalled: false);
        h.Downloads.GetState(Arg.Any<string>()).Returns(call =>
            new ModelDownloadState(call.Arg<string>(), ModelDownloadStatus.Failed, 0, "disk full"));
        h.Downloads.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);

        h.Downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState(h.TranslatorModel.Id, ModelDownloadStatus.Downloading, 50, null));
        Assert.Equal(BannerKind.Downloading, h.Vm.Banner.Kind);
        Assert.InRange(h.Vm.Banner.Progress, 0, 100);

        await h.Vm.DownloadModelsCommand.ExecuteAsync(null);

        Assert.Equal(BannerKind.DownloadFailed, h.Vm.Banner.Kind);
        Assert.Equal("disk full", h.Vm.Banner.Detail);
        Assert.False(h.Vm.ModelsReady);
    }

    [Fact]
    public async Task DownloadStates_OfOtherModels_AreIgnored()
    {
        await using var h = new Harness(modelsInstalled: false);

        h.Downloads.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState("someone-else", ModelDownloadStatus.Downloading, 10, null));

        Assert.NotEqual(BannerKind.Downloading, h.Vm.Banner.Kind);
    }

    [Fact]
    public async Task LongStructuredText_IsTranslatedAsOneDocument()
    {
        await using var h = new Harness();

        h.Vm.SourceText = "# 部署\n- 一\n- 二\n- 三";

        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);
        Assert.Single(h.Vm.Lines);
        Assert.Equal("T:# 部署\n- 一\n- 二\n- 三", h.Vm.Lines[0].Text);
    }

    [Fact]
    public async Task Dispose_StopsObservingTheDraft()
    {
        var h = new Harness();
        await h.DisposeAsync();
        var lines = h.Vm.Lines;

        h.Session.Mutate(draft => draft.Append("之后。"));

        Assert.Same(lines, h.Vm.Lines);
    }

    [Fact]
    public async Task ResultText_IsTheWholeResultOnceReady_AndEmptyWhileSentencesAreStillPending()
    {
        await using var h = new Harness();
        var gate = new TaskCompletionSource<TranslatedText>();
        h.Translator.Handler = (request, ct) => request.Source.StartsWith("先") ? Task.FromResult(new TranslatedText("One.", 1, 2)) : gate.Task;

        h.Vm.SourceText = "先检查缓存。再更新索引。";
        await Wait.Until(() => h.Vm.Lines.Count == 2 && h.Vm.Lines[0].State == LineState.Ready);
        Assert.Equal("", h.Vm.ResultText);

        gate.SetResult(new TranslatedText("Two.", 1, 2));
        await Wait.Until(() => h.Vm.Status == ComposerStatus.Ready);

        Assert.Equal("One. Two.", h.Vm.ResultText);
    }

    [Fact]
    public async Task ResultText_IsEmptyWhileModelsAreMissing()
    {
        await using var h = new Harness(modelsInstalled: false);

        h.Vm.SourceText = "先检查缓存。";
        await Task.Delay(50);

        Assert.Equal("", h.Vm.ResultText);
    }
}
