using LiveLingo.Core.Drafting;

namespace LiveLingo.Core.Tests.Drafting;

public class DraftTests
{
    private static (Draft Draft, JobTicket Ticket) Ready()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("这个 PR 先不要合并。");
        draft.Complete(ticket, UnitKind.Clean, "这个 PR 先不要合并。");
        draft.Complete(ticket, UnitKind.Translation, "Do not merge this PR yet.");
        return (draft, ticket);
    }

    [Fact]
    public void Translation_IsIndependentOfCleanup_AndSelectsTheOutputLanguage()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("先不要合并。");
        draft.Output = OutputKind.Translation;
        draft.Complete(ticket, UnitKind.Translation, "Do not merge yet.");

        Assert.Equal("Do not merge yet.", draft.RequestCommit().Snapshot!.Text);
    }

    [Fact]
    public void ManualCleanEdit_IsNotOverwrittenByLateModelResult()
    {
        var (draft, ticket) = Ready();
        draft.Edit(0, UnitKind.Clean, "请先不要合并这个 PR。");

        Assert.False(draft.Complete(ticket, UnitKind.Clean, "错误的迟到结果"));
        Assert.Equal("请先不要合并这个 PR。", draft.Units[0].Clean.Text);
        Assert.Equal("这个 PR 先不要合并。", draft.Units[0].Raw);
    }

    [Fact]
    public void UseRaw_AffectsOnlyThatSentence()
    {
        var (draft, ticket) = Ready();
        var second = draft.Append("嗯，等 CI 跑完。");
        draft.Complete(second, UnitKind.Clean, "等 CI 跑完。");
        draft.UseRaw(0);

        Assert.False(draft.Complete(ticket, UnitKind.Clean, "不要保留原文"));
        Assert.Equal("这个 PR 先不要合并。等 CI 跑完。", draft.Text());
    }

    [Fact]
    public void RejectedCleanup_ExposesReason_AndRetainsOriginal()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("超时为 30 秒。");
        draft.Complete(ticket, UnitKind.Clean, "超时为 30 分钟。", warning: "候选改动了单位");

        Assert.Equal("超时为 30 秒。", draft.Units[0].Clean.Text);
        Assert.Equal("候选改动了单位", draft.Units[0].Clean.Warning);
        Assert.Equal("超时为 30 秒。", draft.RequestCommit().Snapshot!.Text);
    }

    [Fact]
    public void InvalidateTranslations_DropsOnlyTranslations()
    {
        var (draft, ticket) = Ready();
        draft.InvalidateTranslations();

        Assert.Equal("这个 PR 先不要合并。", draft.Units[0].Clean.Text);
        Assert.False(draft.Complete(ticket, UnitKind.Translation, "wrong language"));
        draft.Output = OutputKind.Translation;
        Assert.Equal(BlockReason.Processing, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void Commit_IsBlockedWhileRecording_AndWhileSelectedOutputIsPending()
    {
        var draft = new Draft(hasTarget: true);
        draft.Append("尚未完成的句子。");

        draft.Recording = true;
        Assert.Equal(BlockReason.Recording, draft.RequestCommit().Blocked);

        draft.Recording = false;
        Assert.Equal(BlockReason.Processing, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void FailedTranslation_IsNotPresentedAsSuccess()
    {
        var (draft, ticket) = Ready();
        draft.InvalidateTranslations();
        draft.Fail(draft.TicketFor(0), UnitKind.Translation, "连接超时");
        draft.Output = OutputKind.Translation;

        Assert.Equal(BlockReason.Unavailable, draft.RequestCommit().Blocked);
        Assert.Equal("连接超时", draft.Units[0].Translation.Error);
        Assert.False(draft.Complete(ticket, UnitKind.Translation, "late"));
    }

    [Fact]
    public void ExplicitRawFallback_CancelsLateCleanup_AndPermitsCommit()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("不要改动接口。");
        draft.UseRaw(0);

        Assert.False(draft.Complete(ticket, UnitKind.Clean, "改动接口。"));
        Assert.Equal("不要改动接口。", draft.RequestCommit().Snapshot!.Text);
    }

    [Fact]
    public void TargetLoss_BlocksInsertion_ButStillAllowsCopy_AndRebindingIsExplicit()
    {
        var (draft, _) = Ready();
        draft.TargetLost = true;

        Assert.Equal(BlockReason.TargetLost, draft.RequestCommit().Blocked);
        Assert.True(draft.ContentReadiness().IsReady);

        draft.Retarget(hasTarget: true);
        Assert.Equal(CommitAction.Insert, draft.RequestCommit().Snapshot!.Action);
    }

    [Fact]
    public void WithoutTarget_CommitIsACopy()
    {
        var (draft, _) = Ready();
        draft.Retarget(hasTarget: false);

        Assert.Equal(CommitAction.Copy, draft.RequestCommit().Snapshot!.Action);
    }

    [Fact]
    public void Snapshot_IsReturnedOnce_AndLateTasksCannotRewriteIt()
    {
        var (draft, ticket) = Ready();
        var first = draft.RequestCommit();

        Assert.True(first.IsSuccess);
        Assert.Same(first, draft.RequestCommit());
        Assert.False(draft.Complete(ticket, UnitKind.Clean, "晚到改写"));
        Assert.Equal("这个 PR 先不要合并。", first.Snapshot!.Text);
    }

    [Fact]
    public void FrozenDraft_RejectsFurtherChanges()
    {
        var (draft, _) = Ready();
        draft.RequestCommit();

        Assert.Throws<InvalidOperationException>(() => draft.Append("新增"));
        Assert.Throws<InvalidOperationException>(() => draft.Edit(0, UnitKind.Clean, "改"));
        Assert.Throws<InvalidOperationException>(() => draft.SyncSource([new SourceUnit("新")], DraftRoute.Units));
    }

    [Fact]
    public void EmptyManualTranslation_IsUnavailable()
    {
        var (draft, _) = Ready();
        draft.Edit(0, UnitKind.Translation, "");
        draft.Output = OutputKind.Translation;

        Assert.Equal(BlockReason.Unavailable, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void Reset_InvalidatesEveryJobOfThePreviousSession()
    {
        var (draft, ticket) = Ready();
        draft.Reset(hasTarget: true);

        Assert.Empty(draft.Units);
        Assert.False(draft.Complete(ticket, UnitKind.Clean, "previous session"));
        Assert.Equal(BlockReason.Empty, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void SyncSource_KeepsEditsAndResultsOfUnchangedSentences()
    {
        var (draft, _) = Ready();
        draft.Edit(0, UnitKind.Clean, "我手动修改的句子。");

        var created = draft.SyncSource(
            [new SourceUnit("这个 PR 先不要合并。"), new SourceUnit("再检查缓存。")], DraftRoute.Units);

        Assert.Single(created);
        Assert.Equal("我手动修改的句子。", draft.Units[0].Clean.Text);
        Assert.Equal("Do not merge this PR yet.", draft.Units[0].Translation.Text);
        Assert.Equal("再检查缓存。", draft.Units[1].Raw);
    }

    [Fact]
    public void LateResult_ForRemovedSentence_CannotLandOnAnotherSentence()
    {
        var draft = new Draft(hasTarget: true);
        var first = draft.SyncSource([new SourceUnit("旧句子。")], DraftRoute.Units).Single();
        draft.SyncSource([new SourceUnit("新句子。")], DraftRoute.Units);

        Assert.False(draft.Complete(first, UnitKind.Clean, "旧结果"));
        Assert.Null(draft.Units[0].Clean.Text);
    }

    [Fact]
    public void DuplicateSentences_EachKeepTheirOwnUnit()
    {
        var draft = new Draft(hasTarget: true);
        draft.SyncSource([new SourceUnit("好的。"), new SourceUnit("好的。")], DraftRoute.Units);
        draft.Edit(1, UnitKind.Clean, "好。");

        draft.SyncSource(
            [new SourceUnit("好的。"), new SourceUnit("好的。"), new SourceUnit("继续。")], DraftRoute.Units);

        Assert.Equal(3, draft.Units.Count);
        Assert.Null(draft.Units[0].Clean.Text);
        Assert.Equal("好。", draft.Units[1].Clean.Text);
    }

    [Fact]
    public void ReplacingTheWholeSource_InvalidatesOldJobs_AndKeepsExactLayout()
    {
        var (draft, ticket) = Ready();
        const string raw = "  {\n    \"message\": \"你好\",\n    \"timeout\": 30\n  }\n";

        draft.SyncSource([new SourceUnit(raw.Trim(), Lead: "  ", Trail: "\n")], DraftRoute.Whole);

        Assert.False(draft.Complete(ticket, UnitKind.Translation, "stale"));
        draft.Output = OutputKind.Raw;
        Assert.Equal(DraftRoute.Whole, draft.Route);
        Assert.Equal(raw, draft.Text());
    }

    [Fact]
    public void EditingSource_NeverRevalidatesALostTarget()
    {
        var (draft, _) = Ready();
        draft.TargetLost = true;
        draft.SyncSource([new SourceUnit("新的输入文本。")], DraftRoute.Units);
        draft.Output = OutputKind.Raw;

        Assert.Equal(BlockReason.TargetLost, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void SyncSource_UpdatesLayoutAndCompletenessOfReusedUnits()
    {
        var draft = new Draft(hasTarget: true);
        draft.SyncSource([new SourceUnit("第一行", IsComplete: false)], DraftRoute.Units);
        var id = draft.Units[0].Id;

        var created = draft.SyncSource([new SourceUnit("第一行", Trail: "\n")], DraftRoute.Units);

        Assert.Empty(created);
        Assert.Equal(id, draft.Units[0].Id);
        Assert.True(draft.Units[0].IsComplete);
        Assert.Equal("\n", draft.Units[0].Trail);
    }

    [Fact]
    public void Text_JoinsSameLineSentencesBySpacingRules_AndKeepsSourceLineBreaks()
    {
        var draft = new Draft(hasTarget: true);
        draft.SyncSource(
            [new SourceUnit("Use Redis."), new SourceUnit("Then retry."), new SourceUnit("好的。", Lead: "\n")],
            DraftRoute.Units);
        draft.Output = OutputKind.Raw;

        Assert.Equal("Use Redis. Then retry.\n好的。", draft.Text());
    }

    [Fact]
    public void Commit_IsEmpty_WhenNothingToWrite()
    {
        var draft = new Draft(hasTarget: true);
        draft.Output = OutputKind.Raw;
        draft.Append("   ");

        Assert.Equal(BlockReason.Empty, draft.RequestCommit().Blocked);
    }

    [Fact]
    public void Edit_ClearsPreviousError()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("好的。");
        draft.Fail(ticket, UnitKind.Clean, "boom");

        draft.Edit(0, UnitKind.Clean, "好。");

        Assert.Equal("", draft.Units[0].Clean.Error);
        Assert.Equal(UnitState.Ready, draft.StateOf(draft.Units[0], OutputKind.Clean));
    }

    [Fact]
    public void SourceRevision_ChangesWithTheSource_NotWithResults()
    {
        var draft = new Draft(hasTarget: true);
        var start = draft.SourceRevision;
        var ticket = draft.Append("好的。");
        var afterAppend = draft.SourceRevision;
        draft.Complete(ticket, UnitKind.Clean, "好。");
        draft.Edit(0, UnitKind.Clean, "好。");
        Assert.Equal(afterAppend, draft.SourceRevision);

        draft.SyncSource([new SourceUnit("好的。")], DraftRoute.Units);
        var afterSync = draft.SourceRevision;
        draft.Reset(hasTarget: true);

        Assert.True(start < afterAppend);
        Assert.True(afterAppend < afterSync);
        Assert.True(afterSync < draft.SourceRevision);
    }

    [Fact]
    public void IsCurrent_FollowsStaleness()
    {
        var (draft, ticket) = Ready();
        Assert.True(draft.IsCurrent(ticket, UnitKind.Translation));

        draft.InvalidateTranslations();
        Assert.False(draft.IsCurrent(ticket, UnitKind.Translation));
        Assert.True(draft.IsCurrent(ticket, UnitKind.Clean));

        draft.Edit(0, UnitKind.Clean, "手动");
        Assert.False(draft.IsCurrent(ticket, UnitKind.Clean));
    }

    [Fact]
    public void Retry_ClearsAFailedResult_ButNeverUserEditedText()
    {
        var draft = new Draft(hasTarget: true);
        var ticket = draft.Append("好的。");
        draft.Fail(ticket, UnitKind.Translation, "boom");

        draft.Retry(0, UnitKind.Translation);

        Assert.Null(draft.Units[0].Translation.Text);
        Assert.Equal("", draft.Units[0].Translation.Error);
        Assert.Equal(UnitState.Pending, draft.StateOf(draft.Units[0], OutputKind.Translation));

        draft.Edit(0, UnitKind.Clean, "好。");
        draft.Retry(0, UnitKind.Clean);
        Assert.Equal("好。", draft.Units[0].Clean.Text);
    }
}
