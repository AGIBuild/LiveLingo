using System.Collections.Concurrent;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Tests.Support;
using LiveLingo.Core.Translation;
using NSubstitute;

namespace LiveLingo.Core.Tests.Drafting;

using Core.Drafting;

public class FinalDocumentTests
{
    private sealed class Polisher : IDictationPolisher
    {
        public ConcurrentQueue<PolishRequest> Calls { get; } = new();
        public Func<PolishRequest, CancellationToken, Task<string>> Answer { get; set; } =
            (r, _) => Task.FromResult(r.Target.Replace("呃，", ""));
        public Task<string> SummarizeAsync(string text, CancellationToken ct) => Task.FromResult("全文主题");
        public Task<string> PolishAsync(PolishRequest request, CancellationToken ct)
        {
            Calls.Enqueue(request);
            return Answer(request, ct);
        }
    }

    private sealed class Translator : ITranslator
    {
        public ConcurrentQueue<TranslationRequest> Calls { get; } = new();
        public Func<TranslationRequest, CancellationToken, Task<string>> Answer { get; set; } =
            (r, _) => Task.FromResult(r.Source);
        public async Task<TranslatedText> TranslateAsync(TranslationRequest request, CancellationToken ct)
        {
            Calls.Enqueue(request);
            return new TranslatedText(await Answer(request, ct), 0, 0);
        }
    }

    private static DraftSession Session(OutputKind output = OutputKind.Clean)
    {
        var session = new DraftSession(false);
        session.Mutate(d => { d.DocumentMode = true; d.Output = output; });
        return session;
    }

    private static async Task Wait(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Document condition timed out.");
            await Task.Delay(5);
        }
    }

    private static Task Ready(DraftSession session, UnitKind kind) =>
        Wait(() => session.Read(d => d.Final(kind).State is DocumentState.Ready or DocumentState.Failed));

    [Fact]
    public async Task Polish_FinalIsIndependentOfSourceUnits_AndStopBlocksSynchronously()
    {
        var session = Session();
        var polisher = new Polisher();
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        session.Mutate(d =>
        {
            d.Recording = true;
            d.Append("呃，我觉得。");
            d.Append("应该先检查 `cache`。");
            d.Append("不要修改接口。");
        });
        var original = session.Read(d => d.SourceText());
        var ids = session.Read(d => d.Units.Select(u => u.Id).ToArray());
        session.Mutate(d =>
        {
            d.Recording = false;
            Assert.Equal(DocumentState.Pending, d.Final(UnitKind.Clean).State);
            Assert.Equal(BlockReason.Processing, d.RequestCommit().Blocked);
        });
        await Ready(session, UnitKind.Clean);

        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Equal(original, polisher.Calls.Single().Target);
        Assert.Equal(original, session.Read(d => d.SourceText()));
        Assert.Equal(ids, session.Read(d => d.Units.Select(u => u.Id).ToArray()));
        Assert.Equal(original.Replace("呃，", ""), session.Read(d => d.Text()));
        Assert.Equal(session.Read(d => d.SourceRevision), session.Read(d => d.Final(UnitKind.Clean).InputRevision));
        Assert.True(session.Mutate(d => d.RequestCommit()).IsSuccess);
        Assert.False(session.Mutate(d => d.Complete(d.TicketFor(0), UnitKind.Clean, "late")));
    }

    [Fact]
    public async Task TypedDocument_IsDebounced_AndDoesNotNeedUnitCleanup()
    {
        var session = Session();
        var time = new ManualTimeProvider();
        var polisher = new Polisher();
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300, time: time);
        session.Mutate(d => d.SyncSource(TextUnits.Split("呃，先检查。然后发布。"), DraftRoute.Units));
        Assert.Empty(polisher.Calls);
        time.Advance(TimeSpan.FromMilliseconds(399));
        time.Timer.Fire();
        Assert.Empty(polisher.Calls);
        time.Advance(TimeSpan.FromMilliseconds(1));
        time.Timer.Fire();
        await Ready(session, UnitKind.Clean);
        Assert.Single(polisher.Calls);
        Assert.Equal("先检查。然后发布。", session.Read(d => d.Text()));
        Assert.All(session.Read(d => d.Units), u => Assert.Null(u.Clean.Text));
    }

    [Theory]
    [InlineData(TargetLanguage.English)]
    [InlineData(TargetLanguage.Chinese)]
    public async Task DirectTranslation_UsesWholeExpression_WithoutCleaner(TargetLanguage target)
    {
        var session = Session(OutputKind.Translation);
        var translator = new Translator();
        await using var scheduler = new TranslationScheduler(session, translator, target, TimeSpan.FromMilliseconds(400));
        session.Mutate(d =>
        {
            d.Append("我觉得。");
            d.Append("应该先检查 `cache`。");
            d.Append("不要修改接口。");
        });
        var source = session.Read(d => d.SourceText());
        await Ready(session, UnitKind.Translation);
        Assert.Equal(source, translator.Calls.Single().Source);
        Assert.Equal(target, translator.Calls.Single().Target);
        Assert.Equal(3, session.Read(d => d.Units.Count));
        Assert.Equal(source, session.Read(d => d.SourceText()));
        Assert.Contains("`cache`", session.Read(d => d.Text()));
        Assert.NotEqual(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.True(translator.Calls.Single().FinalDocument);
    }

    [Fact]
    public async Task SameLanguageRefusalBlocksUntilExplicitCleanChoice_NoHiddenCleanerOrRetry()
    {
        var session = Session(OutputKind.Translation);
        session.Mutate(d =>
        {
            d.Append("呃，我不确定什么地方合适。");
            d.Append("请你找一个合适的位置，提供手动修复能力。");
            d.Append("方案要用户体验友好，真正修复问题，而不是只提示。");
        });
        var original = session.Read(d => d.SourceText());
        var units = session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray());
        var client = Substitute.For<IChatCompletionClient>();
        client.CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletion("你在合适的位置提醒用户。", 0, 0, 0));
        var polisher = new Polisher();
        await using var polish = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        await using var translation = new TranslationScheduler(session, new LlmTranslator(client),
            TargetLanguage.Chinese, TimeSpan.Zero);
        await Ready(session, UnitKind.Translation);

        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Translation).State));
        Assert.Contains("Final same-language translation refused", session.Read(d => d.Final(UnitKind.Translation).Error));
        Assert.Equal(OutputKind.Translation, session.Read(d => d.Output));
        Assert.Equal(BlockReason.Unavailable, session.Read(d => d.ContentReadiness().Reason));
        Assert.Equal(BlockReason.Unavailable, session.Mutate(d => d.RequestCommit()).Blocked);
        Assert.Null(session.Read(d => d.Snapshot));
        Assert.Empty(polisher.Calls);
        Assert.Equal(original, session.Read(d => d.SourceText()));
        Assert.Equal(units, session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray()));

        // More notifications do not implicitly choose Clean or retry the failed translator.
        session.Mutate(d => d.Output = OutputKind.Translation);
        await Task.Delay(30);
        Assert.Empty(polisher.Calls);
        await client.Received(1).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());

        session.Mutate(d =>
        {
            d.Output = OutputKind.Clean; // The user's explicit 整理原话 choice.
            Assert.Equal(BlockReason.Processing, d.ContentReadiness().Reason);
        });
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.True(session.Read(d => d.ContentReadiness().IsReady));
        Assert.Equal(original.Replace("呃，", ""), session.Read(d => d.Text()));
        Assert.Contains("我不确定", session.Read(d => d.Text()));
        Assert.Contains("用户体验友好", session.Read(d => d.Text()));
        Assert.Contains("而不是只提示", session.Read(d => d.Text()));
        Assert.StartsWith(PolishStyle.Default, polisher.Calls.Single().Style);
        Assert.Equal(original, session.Read(d => d.SourceText()));
        Assert.Equal(units, session.Read(d => d.Units.Select(u => (u.Id, u.Raw, u.Lead, u.Trail)).ToArray()));
        await client.Received(1).CompleteAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal(original.Replace("呃，", ""), session.Mutate(d => d.RequestCommit()).Snapshot!.Text);
    }

    [Fact]
    public async Task RecordingUsesUnitPreviews_StopUsesIndependentFinal()
    {
        var session = Session(OutputKind.Translation);
        var translator = new Translator();
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        session.Mutate(d => { d.Recording = true; d.Append("先检查。"); d.Append("再发布。"); });
        await Wait(() => session.Read(d => d.Units.All(u => u.Translation.Text is not null)));
        Assert.Equal(2, translator.Calls.Count);
        session.Mutate(d => d.Recording = false);
        await Ready(session, UnitKind.Translation);
        Assert.Equal("先检查。再发布。", translator.Calls.Last().Source);
        Assert.Equal(3, translator.Calls.Count);
    }

    [Fact]
    public void EditingGuardsCallbacksAndCommit_SaveIgnoresUnrelatedResultChanges()
    {
        var draft = new Draft(false) { DocumentMode = true };
        draft.Append("原文。");
        var model = draft.DocumentTicketFor(UnitKind.Clean);
        var unit = draft.TicketFor(0);
        var edit = draft.BeginDocumentEdit(UnitKind.Clean);
        Assert.True(draft.DocumentEditing);
        Assert.False(draft.CompleteDocument(model, "迟到。"));
        Assert.False(draft.Complete(unit, UnitKind.Translation, "late"));
        Assert.Equal(BlockReason.Processing, draft.RequestCommit().Blocked);
        draft.InvalidateTranslations();
        draft.Output = OutputKind.Translation;
        Assert.True(draft.SaveDocumentEdit(edit, "手动终稿。"));
        Assert.False(draft.DocumentEditing);
        Assert.True(draft.Final(UnitKind.Clean).IsOwned);
        Assert.Equal(DocumentState.Ready, draft.Final(UnitKind.Clean).State);
        draft.Output = OutputKind.Clean;
        Assert.Equal("手动终稿。", draft.Text());
    }

    [Fact]
    public void SourceChangeClearAndCancelInvalidateEditAndModelTickets()
    {
        var draft = new Draft(false) { DocumentMode = true };
        draft.Append("原文。");
        var edit = draft.BeginDocumentEdit(UnitKind.Clean);
        draft.Append("新增。");
        Assert.False(draft.SaveDocumentEdit(edit, "wrong source"));
        Assert.False(draft.DocumentEditing);
        var second = draft.BeginDocumentEdit(UnitKind.Clean);
        draft.CancelDocumentEdit(second);
        Assert.False(draft.SaveDocumentEdit(second, "cancelled"));
        var old = draft.DocumentTicketFor(UnitKind.Clean);
        var third = draft.BeginDocumentEdit(UnitKind.Clean);
        draft.Reset(false);
        Assert.True(draft.DocumentMode);
        draft.Append("新稿。");
        Assert.False(draft.SaveDocumentEdit(third, "previous epoch"));
        Assert.False(draft.CompleteDocument(old, "previous epoch"));
    }

    [Fact]
    public void OwnedResultsBecomeStale_SourceTargetOrCleanChangeNeverReleasesOwnership()
    {
        var draft = new Draft(false) { DocumentMode = true, Output = OutputKind.Translation };
        draft.Append("原文。");
        Assert.True(draft.SaveDocumentEdit(draft.BeginDocumentEdit(UnitKind.Translation), "My final."));
        draft.Output = OutputKind.Clean;
        draft.Output = OutputKind.Translation;
        Assert.Equal("My final.", draft.Text());
        draft.InvalidateTranslations();
        Assert.Equal(DocumentState.Stale, draft.Final(UnitKind.Translation).State);
        Assert.True(draft.Final(UnitKind.Translation).IsOwned);
        Assert.False(draft.DocumentDue(UnitKind.Translation));
        draft.SaveDocumentEdit(draft.BeginDocumentEdit(UnitKind.Clean), "手动原文。");
        Assert.True(draft.Final(UnitKind.Translation).IsOwned);
        draft.Append("新增。");
        Assert.Equal(DocumentState.Stale, draft.Final(UnitKind.Clean).State);
        Assert.Equal("My final.", draft.Final(UnitKind.Translation).Text);
        Assert.Equal(BlockReason.Unavailable, draft.ContentReadiness().Reason);
        draft.RetryDocument(UnitKind.Translation);
        Assert.False(draft.Final(UnitKind.Translation).IsOwned);
        Assert.Equal(DocumentState.Pending, draft.Final(UnitKind.Translation).State);
        Assert.True(draft.DocumentDue(UnitKind.Translation));
    }

    [Fact]
    public async Task FailedFinalBlocksUntilExplicitCompletePreviewAcceptance()
    {
        var session = Session();
        session.Mutate(d =>
        {
            d.Append("先检查。");
            d.Append("再发布。");
            d.Complete(d.TicketFor(0), UnitKind.Clean, "先检查。");
        });
        var polisher = new Polisher { Answer = (_, _) => throw new HttpRequestException("offline") };
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Equal("offline", session.Read(d => d.Final(UnitKind.Clean).Error));
        Assert.Equal(BlockReason.Unavailable, session.Read(d => d.ContentReadiness().Reason));
        Assert.False(session.Mutate(d => d.AcceptPreview(UnitKind.Clean)));
        session.Mutate(d => d.Complete(d.TicketFor(1), UnitKind.Clean, "再发布。"));
        Assert.True(session.Mutate(d => d.AcceptPreview(UnitKind.Clean)));
        Assert.True(session.Read(d => d.Final(UnitKind.Clean).IsOwned));
        Assert.Equal("先检查。再发布。", session.Mutate(d => d.RequestCommit()).Snapshot!.Text);
        Assert.Single(polisher.Calls);
    }

    [Fact]
    public void PreviewMustBeNonemptyAndComplete_ManualFinalCanOverrideFailure()
    {
        var draft = new Draft(false) { DocumentMode = true };
        draft.SyncSource([new SourceUnit("未完", IsComplete: false)], DraftRoute.Units);
        draft.Edit(0, UnitKind.Clean, " ");
        Assert.False(draft.AcceptPreview(UnitKind.Clean));
        draft.SaveDocumentEdit(draft.BeginDocumentEdit(UnitKind.Clean), "手动终稿");
        Assert.True(draft.ContentReadiness().IsReady);
    }

    [Fact]
    public void SnapshotIsDetached_RestoresManualFinalLayoutUnitsAndInvalidatesEpoch()
    {
        var draft = new Draft(true) { DocumentMode = true };
        draft.SyncSource(TextUnits.Split("  原文。\n\n保留 `code`。\n"), DraftRoute.Whole);
        draft.Edit(0, UnitKind.Clean, "手动预览。");
        draft.SaveDocumentEdit(draft.BeginDocumentEdit(UnitKind.Clean), "手动终稿。\n保留 `code`。");
        var original = draft.SourceText();
        var state = draft.CaptureState();
        var ticket = draft.DocumentTicketFor(UnitKind.Clean);
        draft.BeginDocumentEdit(UnitKind.Translation);
        draft.Recording = true;
        draft.Polishing = true;
        draft.RestoreState(state);
        Assert.True(draft.DocumentMode);
        Assert.False(draft.DocumentEditing);
        Assert.False(draft.Recording);
        Assert.False(draft.Polishing);
        Assert.Null(draft.Snapshot);
        Assert.Equal(original, draft.SourceText());
        Assert.Equal("手动预览。", draft.Units[0].Clean.Text);
        Assert.True(draft.Units[0].Clean.IsOwned);
        Assert.Equal("手动终稿。\n保留 `code`。", draft.Text());
        Assert.True(draft.Final(UnitKind.Clean).IsOwned);
        Assert.False(draft.CompleteDocument(ticket, "late"));
        var legacy = new Draft(false);
        legacy.RestoreState(state);
        Assert.False(legacy.DocumentMode);
        Assert.False(legacy.HasTarget);
    }

    [Fact]
    public void SnapshotSettlesRecognizedTailAsSource_AndReRequestsPendingFinal()
    {
        var draft = new Draft(false) { DocumentMode = true, Recording = true };
        draft.Append("已经说完。");
        draft.SetPending("还没有结束的话");
        var source = draft.SourceText();
        var saved = draft.CaptureState();
        draft.RestoreState(saved);
        Assert.Equal(source, draft.SourceText());
        Assert.Equal("", draft.Pending);
        Assert.Equal(2, draft.Units.Count);
        Assert.False(draft.Recording);
        Assert.Equal(DocumentState.Pending, draft.Final(UnitKind.Clean).State);
        Assert.True(draft.DocumentDue(UnitKind.Clean));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongSingleUnit_IsGroupedWithoutOmission_AndKeepsLiteralsAndLayout(bool translation)
    {
        var session = Session(translation ? OutputKind.Translation : OutputKind.Clean);
        var source = "  " + string.Concat(Enumerable.Range(0, 130)
            .Select(i => $"第{i}项保留 `value{i}`。\n")) + "\n结束。\n";
        session.Mutate(d => d.Append(source));
        var translator = new Translator();
        var polisher = new Polisher();
        await using var scheduler = translation
            ? (IAsyncDisposable)new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero)
            : new PolishScheduler(session, polisher, [], PolishStyle.Default, 300);
        var kind = translation ? UnitKind.Translation : UnitKind.Clean;
        await Ready(session, kind);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(kind).State));
        Assert.Equal(source, session.Read(d => d.Text()));
        Assert.Single(session.Read(d => d.Units));
        Assert.Equal(source, session.Read(d => d.SourceText()));
        if (translation)
        {
            Assert.True(translator.Calls.Count >= 3);
            Assert.All(translator.Calls, r => Assert.InRange(r.Source.Length, 1, 800));
            Assert.NotEmpty(translator.Calls.Last().Context);
        }
        else
        {
            Assert.True(polisher.Calls.Count >= 3);
            Assert.All(polisher.Calls, r => Assert.InRange(r.Target.Length, 1, 800));
            Assert.NotEmpty(polisher.Calls.Last().Preceding);
        }
    }

    [Fact]
    public async Task OversizedProtectedLiteral_FailsExplicitlyWithoutModelCall()
    {
        var session = Session(OutputKind.Translation);
        session.Mutate(d => d.Append("保留 `" + new string('x', 801) + "`。"));
        var translator = new Translator();
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Ready(session, UnitKind.Translation);
        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Translation).State));
        Assert.Contains("literal", session.Read(d => d.Final(UnitKind.Translation).Error));
        Assert.Empty(translator.Calls);
    }

    [Theory]
    [InlineData(UnitKind.Clean)]
    [InlineData(UnitKind.Translation)]
    public async Task UnitOwnershipIsProtectedInFinalAssembly(UnitKind kind)
    {
        var session = Session(kind == UnitKind.Clean ? OutputKind.Clean : OutputKind.Translation);
        session.Mutate(d =>
        {
            d.Append("前文。");
            d.Append("不要覆盖这段。");
            d.Append("后文。");
            d.Edit(1, kind, "MY MANUAL RESULT");
        });
        var translator = new Translator();
        var polisher = new Polisher();
        await using var scheduler = kind == UnitKind.Clean
            ? (IAsyncDisposable)new PolishScheduler(session, polisher, [], PolishStyle.Default, 300)
            : new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Ready(session, kind);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(kind).State));
        Assert.Equal("前文。MY MANUAL RESULT后文。", session.Read(d => d.Text()));
        Assert.DoesNotContain("不要覆盖这段", string.Join("", translator.Calls.Select(r => r.Source)));
        Assert.DoesNotContain("不要覆盖这段", string.Join("", polisher.Calls.Select(r => r.Target)));
        Assert.Equal("MY MANUAL RESULT", session.Read(d => d.Units[1].Result(kind).Text));
    }

    [Fact]
    public async Task CleanFinalIsDeterministicTranslationInput_LateChangesRejectCallbacks()
    {
        var session = Session();
        session.Mutate(d =>
        {
            d.Append("呃，原文。");
            d.SaveDocumentEdit(d.BeginDocumentEdit(UnitKind.Clean), "原文。");
            d.Output = OutputKind.Translation;
        });
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new Translator { Answer = (r, _) => r.Source == "原文。" ? first.Task : Task.FromResult(r.Source) };
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Wait(() => translator.Calls.Count == 1);
        Assert.Equal("原文。", translator.Calls.Single().Source);
        session.Mutate(d => d.SaveDocumentEdit(d.BeginDocumentEdit(UnitKind.Clean), "新原文。"));
        first.SetResult("old final");
        await Ready(session, UnitKind.Translation);
        Assert.Equal(2, translator.Calls.Count);
        Assert.Equal("新原文。", session.Read(d => d.Text()));
        session.Mutate(d => d.SaveDocumentEdit(d.BeginDocumentEdit(UnitKind.Translation), "OWNED"));
        session.Mutate(d => d.SaveDocumentEdit(d.BeginDocumentEdit(UnitKind.Clean), "又修改。"));
        await Task.Delay(30);
        Assert.Equal(2, translator.Calls.Count);
        Assert.Equal(DocumentState.Stale, session.Read(d => d.Final(UnitKind.Translation).State));
        Assert.Equal("OWNED", session.Read(d => d.Final(UnitKind.Translation).Text));
    }

    [Fact]
    public async Task TargetChangeAndRetryRejectLateUncooperativeModelCallbacks()
    {
        var session = Session(OutputKind.Translation);
        session.Mutate(d => d.Append("保留原文。"));
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var translator = new Translator { Answer = (r, _) => r.Target == TargetLanguage.English
            ? pending.Task : Task.FromResult("中文终稿。") };
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Wait(() => translator.Calls.Count == 1);
        scheduler.SetTarget(TargetLanguage.Chinese);
        pending.SetResult("late English");
        await Ready(session, UnitKind.Translation);
        Assert.Equal("中文终稿。", session.Read(d => d.Text()));
        session.Mutate(d => d.RetryDocument(UnitKind.Translation));
        await Ready(session, UnitKind.Translation);
        Assert.Equal(3, translator.Calls.Count);
    }

    [Fact]
    public void EditingCleanInvalidatesDependentNonownedUnitTranslation()
    {
        var draft = new Draft(false) { DocumentMode = true };
        draft.Append("原文。");
        draft.Edit(0, UnitKind.Clean, "原文。");
        var translation = draft.TicketFor(0);
        draft.Complete(translation, UnitKind.Translation, "old");
        draft.Edit(0, UnitKind.Clean, "新原文。");
        Assert.Null(draft.Units[0].Translation.Text);
        Assert.False(draft.Complete(translation, UnitKind.Translation, "late"));
        draft.Edit(0, UnitKind.Translation, "owned");
        draft.Edit(0, UnitKind.Clean, "再改。");
        Assert.Equal("owned", draft.Units[0].Translation.Text);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task RejectedFinal_HasBoundedRetriesAndNoSuccessShapedFallback(bool custom, int calls)
    {
        var session = Session();
        session.Mutate(d =>
        {
            d.Append("端口是 8080，不要修改。");
            d.Complete(d.TicketFor(0), UnitKind.Clean, "端口是 8080，不要修改。");
        });
        var polisher = new Polisher { Answer = (_, _) => Task.FromResult("修改端口。") };
        await using var scheduler = new PolishScheduler(session, polisher, [],
            custom ? "自定义要求：请保留所有原始事实。" : PolishStyle.Default, 300);
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.False(session.Read(d => d.ContentReadiness().IsReady));
        Assert.Equal(calls, polisher.Calls.Count);
        Assert.NotEmpty(session.Read(d => d.Final(UnitKind.Clean).Error));
        Assert.Equal("端口是 8080，不要修改。", session.Read(d => d.PreviewText(UnitKind.Clean)));
    }

    [Fact]
    public async Task CancelledOutputChoiceCannotAcceptLateCallback_AfterSwitchingBack()
    {
        var session = Session(OutputKind.Translation);
        session.Mutate(d => d.Append("保留原文。"));
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var translator = new Translator { Answer = (_, _) => Interlocked.Increment(ref calls) == 1
            ? pending.Task : Task.FromResult("fresh") };
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Wait(() => translator.Calls.Count == 1);
        session.Mutate(d => d.Output = OutputKind.Raw);
        session.Mutate(d => d.Output = OutputKind.Translation);
        pending.SetResult("cancelled");
        await Ready(session, UnitKind.Translation);
        Assert.Equal(2, translator.Calls.Count);
        Assert.Equal("fresh", session.Read(d => d.Text()));
    }

    [Fact]
    public async Task LongEnglishWordSplit_DoesNotInsertOrDropCharacters()
    {
        var session = Session(OutputKind.Translation);
        var source = new string('x', 2401);
        session.Mutate(d => d.Append(source));
        var translator = new Translator();
        await using var scheduler = new TranslationScheduler(session, translator, TargetLanguage.English, TimeSpan.Zero);
        await Ready(session, UnitKind.Translation);
        Assert.Equal(source, session.Read(d => d.Text()));
        Assert.All(translator.Calls, r => Assert.InRange(r.Source.Length, 1, 800));
    }

    [Fact]
    public void IdenticalSourceReconciliationDoesNotStaleManualFinal()
    {
        var draft = new Draft(false) { DocumentMode = true };
        draft.Append("原文。");
        draft.SaveDocumentEdit(draft.BeginDocumentEdit(UnitKind.Clean), "终稿。");
        var revision = draft.SourceRevision;
        draft.SyncSource(TextUnits.Split("原文。"), DraftRoute.Units);
        Assert.Equal(revision, draft.SourceRevision);
        Assert.Equal(DocumentState.Ready, draft.Final(UnitKind.Clean).State);
        Assert.Equal("终稿。", draft.Text());
    }

    [Fact]
    public async Task MultiScenarioPolish_SynthesizesHolisticallyAcrossNewlines_CachesPerMode_AndIsolatesTranslationInput()
    {
        var session = Session();
        var polisher = new Polisher
        {
            Answer = (req, _) => Task.FromResult(req.Gate == PolishGateKind.FactAnchor
                ? (req.Style.Contains("待办与派单", StringComparison.Ordinal)
                    ? "- [ ] 将 `CacheService` 超时设为 30 秒"
                    : "### 任务目标\n排查 `CacheService` 并保持 30 秒超时。")
                : req.Target.Replace("呃，", ""))
        };
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishPresets.BuiltIns, 300);

        // Multi-line source over 800 chars (1,000 chars) stays ONE holistic call in AI Task mode instead of splitting per newline.
        var multiLine = "第一行铺垫：" + new string('背', 480) + "\n第二行核心：把 `CacheService` 超时设为 30 秒，" + new string('细', 480);
        session.Mutate(d =>
        {
            d.SetPolishMode(PolishPresets.AiTaskId);
            d.Append(multiLine);
        });
        await Ready(session, UnitKind.Clean);
        Assert.Single(polisher.Calls);
        Assert.Equal("### 任务目标\n排查 `CacheService` 并保持 30 秒超时。", session.Read(d => d.Text()));

        // Manually edit AI Task mode, switch to Todo, then switch back -> manual AI Task edit is preserved without extra LLM call!
        session.Mutate(d => d.SaveDocumentEdit(d.BeginDocumentEdit(UnitKind.Clean), "### 任务目标\n人工确认过的 `CacheService` 30 秒指令。"));
        session.Mutate(d => d.SetPolishMode(PolishPresets.TodoId));
        await Ready(session, UnitKind.Clean);
        Assert.Equal("- [ ] 将 `CacheService` 超时设为 30 秒", session.Read(d => d.Text()));

        var callsBeforeBack = polisher.Calls.Count;
        session.Mutate(d => d.SetPolishMode(PolishPresets.AiTaskId));
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Equal("### 任务目标\n人工确认过的 `CacheService` 30 秒指令。", session.Read(d => d.Text()));
        Assert.Equal(callsBeforeBack, polisher.Calls.Count);

        // TranslationDocumentInput NEVER reads structured AI Task or Todo output; it only consumes "natural" verbatim cleanup.
        Assert.Null(session.Read(d => d.TranslationDocumentInput()));
        session.Mutate(d => d.SetPolishMode(PolishPresets.NaturalId));
        await Ready(session, UnitKind.Clean);
        Assert.NotNull(session.Read(d => d.TranslationDocumentInput()));
        Assert.DoesNotContain("### 任务目标", session.Read(d => d.TranslationDocumentInput())!);
    }

    [Fact]
    public async Task StructuredMode_PerformsOneTargetedSelfRepairRetryInSameMode_WhenFirstOutputViolatesGate()
    {
        var session = Session();
        var attempt = 0;
        var polisher = new Polisher
        {
            Answer = (req, _) =>
            {
                if (Interlocked.Increment(ref attempt) == 1)
                    return Task.FromResult("好的，下面是为你编写的代码：\n```csharp\nvar x = 30;\n```");
                Assert.Contains("越界回答", req.RepairFeedback);
                return Task.FromResult("### 任务目标\n将 `CacheService` 超时设为 30 秒，不要直接改代码。");
            }
        };
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishPresets.BuiltIns, 300);

        session.Mutate(d =>
        {
            d.SetPolishMode(PolishPresets.AiTaskId);
            d.Append("帮我把 `CacheService` 超时设为 30 秒，先给方案不要直接改代码。");
        });
        await Ready(session, UnitKind.Clean);

        Assert.Equal(2, polisher.Calls.Count);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Equal("### 任务目标\n将 `CacheService` 超时设为 30 秒，不要直接改代码。", session.Read(d => d.Text()));
    }

    [Fact]
    public async Task SwitchingPolishMode_StartsImmediatelyWithoutTypingDebounce_AndReSelectingFailedModeRetries()
    {
        var session = Session();
        var shouldFailTodo = true;
        var polisher = new Polisher
        {
            Answer = (req, _) =>
            {
                if (req.Style.Contains("待办与派单", StringComparison.Ordinal))
                {
                    if (shouldFailTodo)
                        throw new HttpRequestException("temporary failure");
                    return Task.FromResult("- [ ] 确认 18 号下班放假与 19 号至 3 号排班");
                }
                return Task.FromResult("确认十八号下班放假及十九号至三号的休假安排。");
            }
        };
        var fakeClock = new LiveLingo.Core.Tests.Support.ManualTimeProvider();
        await using var scheduler = new PolishScheduler(
            session, polisher, [], PolishPresets.BuiltIns, 300, time: fakeClock);

        // SyncSource bumps EditRevision, which normally starts a 400 ms typing pause; switching polish mode must bypass it immediately (0 ms).
        session.Mutate(d => d.SyncSource(TextUnits.Split("十八号下了班就放假了吗？十九号休到三号，对吧？"), DraftRoute.Units));
        session.Mutate(d => d.SetPolishMode(PolishPresets.TodoId));
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Clean).State));

        // Switching to formal succeeds immediately without advancing fakeClock, and switching back to failed todo retries automatically.
        shouldFailTodo = false;
        session.Mutate(d => d.SetPolishMode(PolishPresets.FormalId));
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));

        session.Mutate(d => d.SetPolishMode(PolishPresets.TodoId));
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Ready, session.Read(d => d.Final(UnitKind.Clean).State));
        Assert.Contains("18", session.Read(d => d.Text()));
    }

    [Fact]
    public async Task SoftGateWarning_PreservesGeneratedDocumentWithWarning_WhileHardHijackStillFails_AndCleanPreviewNeverTurnsBlank()
    {
        var session = Session();
        session.Mutate(d => d.AppendDictated("讨论一下多智能体调度，跑五次有四次成功。"));
        // Before any unit cleanup runs, PreviewText(Clean) falls back to Raw instead of returning an empty string.
        Assert.Equal("讨论一下多智能体调度，跑五次有四次成功。", session.Read(d => d.PreviewText(UnitKind.Clean)));

        var polisher = new Polisher
        {
            Answer = (req, _) =>
            {
                if (req.Style.Contains("AI 任务指令", StringComparison.Ordinal))
                    return Task.FromResult("```python\nprint('hijack')\n```");
                return Task.FromResult("### 核心结论\n多智能体调度在 99% 虚构指标下稳定。");
            }
        };
        await using var scheduler = new PolishScheduler(session, polisher, [], PolishPresets.BuiltIns, 300);

        // Soft warning ("99" invented number in report mode after 1x repair): keeps generated document Ready + Warning!
        session.Mutate(d => d.SetPolishMode(PolishPresets.ReportId));
        await Ready(session, UnitKind.Clean);
        var reportFinal = session.Read(d => d.Final(UnitKind.Clean));
        Assert.Equal(DocumentState.Ready, reportFinal.State);
        Assert.Contains("核心结论", reportFinal.Text);
        Assert.Equal(new GateRejection(GateReason.NumberInvented, "99"), reportFinal.Warning);

        // Hard failure (ExecutionHijack code block in ai-task mode after 1x repair): blocked as Failed!
        session.Mutate(d => d.SetPolishMode(PolishPresets.AiTaskId));
        await Ready(session, UnitKind.Clean);
        Assert.Equal(DocumentState.Failed, session.Read(d => d.Final(UnitKind.Clean).State));
    }
}
