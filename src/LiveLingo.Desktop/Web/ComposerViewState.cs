using LiveLingo.Desktop.Composer;

namespace LiveLingo.Desktop.Web;

internal sealed record DestState(string Text, string Kind);

internal sealed record PrivacyState(string Text, bool Cloud, string Aria);

internal sealed record MenuItemState(string Id, string Label, bool Checked, bool Enabled);

/// <param name="Note">Whose choices are remembered ("Your choice is remembered for Slack"); null when remembering is off or unknown.</param>
internal sealed record OutputState(string Value, string Pill, bool Globe, IReadOnlyList<MenuItemState> Menu, string Swap, string? Note);

internal sealed record ActionState(string Id, string Label);

internal sealed record BannerView(string Kind, string Tone, string Message, double Progress, IReadOnlyList<ActionState> Actions);

internal sealed record SourceLineState(string Time, string Text, bool Tail);

/// <summary>The last draft was closed without being written: the empty composer offers to continue it (never automatically).</summary>
/// <param name="Index">The draft's index in the recent drafts, for the <c>restoreRecent</c> intent.</param>
internal sealed record ResumeState(int Index, string Label);

/// <summary>The composer stands on a choice remembered for the application: the note says so and offers the way back.</summary>
internal sealed record RememberedState(string Text, string Action);

/// <param name="Mode">"edit" shows the text box, "live" the listening or transcript lines.</param>
/// <param name="SourceRev">The page adopts <paramref name="Text"/> only when this is newer than its own last revision (design 5.3).</param>
/// <param name="Resume">Offered together with the hint of an empty composer; null otherwise.</param>
/// <param name="Remembered">Said together with the hint of an empty composer that stands on a remembered choice; null otherwise.</param>
internal sealed record SourceState(
    string Mode, string Text, int SourceRev, bool Editable, string Label, bool RouteChip, bool Hint, IReadOnlyList<SourceLineState> Lines,
    ResumeState? Resume, RememberedState? Remembered, bool CanClear);

internal sealed record RowState(
    int I, string State, string Text, string Note, string Time, bool Warn, bool Owned, bool Editing,
    bool CanEdit, bool CanUseOriginal, string UseOriginalAria, string EditAria);

/// <param name="EditText">The page takes it once, when a sentence starts being edited.</param>
internal sealed record ResultState(bool Visible, string Label, IReadOnlyList<RowState> Rows, string EditText);

internal sealed record MicState(bool Recording, string Aria, bool Enabled);

internal sealed record StatusState(string Text, string Tone);

/// <summary>The models downloading in the background: the footer shows it so nobody waits without knowing.</summary>
internal sealed record DownloadState(string Text, int Percent);

internal sealed record CommitState(string Label, bool Enabled, bool Armed, bool Written, string Aria);

internal sealed record FooterState(MicState Mic, StatusState Status, DownloadState? Download, bool CopyEnabled, CommitState Commit);

internal sealed record LayoutState(bool Wide);

/// <param name="Theme">"Auto", "Light" or "Dark".</param>
/// <param name="Modifier">"meta" (Command, macOS) or "ctrl": the modifier of every shortcut.</param>
internal sealed record OptionsState(
    bool Animations, string Theme, string CommitKey, string CommitKeyCap, string NewlineKeyCap, string Modifier,
    IReadOnlyList<ShortcutState> Shortcuts);

/// <summary>A keyboard shortcut of the page (<see cref="ComposerShortcuts"/>), with its key cap for menus and hints.</summary>
internal sealed record ShortcutState(string Id, string Code, bool Shift, string Cap);

internal sealed record MenusState(IReadOnlyList<MenuItemState> Target, IReadOnlyList<MenuItemState> More);

internal sealed record RecentItemState(int Index, string Label);

/// <param name="Last">The format the user exported last (<c>txt</c>, <c>markdown</c> or <c>srt</c>), which the dialog offers first; null before any export.</param>
internal sealed record ExportState(bool Txt, bool Markdown, bool Srt, string? Last);

internal sealed record DialogsState(IReadOnlyList<RecentItemState> Recent, bool CanRestore, ExportState Export);

/// <summary>
/// Everything the composer page draws, as plain data (design 5.2). The page holds no business state of its own; it is
/// replaced wholesale by each snapshot. Built only by <see cref="From"/>.
/// </summary>
internal sealed record ComposerViewState(
    int Rev,
    DestState Dest,
    PrivacyState Privacy,
    OutputState Output,
    BannerView? Banner,
    SourceState Source,
    ResultState Result,
    FooterState Footer,
    LayoutState Layout,
    OptionsState Options,
    MenusState Menus,
    DialogsState Dialogs)
{
    public static ComposerViewState From(ComposerViewModel vm, ComposerTexts texts, RecentDrafts recent, int sourceRev)
    {
        var live = vm.IsListening || vm.IsStartingListening || vm.HasTimeline;
        var empty = vm.Lines.Count == 0 && !live && string.IsNullOrWhiteSpace(vm.SourceText);
        var hasResult = vm.Lines.Count > 0 && vm.Output != ComposerOutput.Original;
        var cloud = vm.CloudTranslate || vm.CloudClean;
        var armed = vm.IsWriteQueued;
        var targetName = vm.TargetName;

        return new ComposerViewState(
            Rev: 0,
            Dest: new DestState(
                texts.Dest(vm.HasTarget, vm.IsTargetLost, targetName),
                vm.IsTargetLost ? "lost" : vm.HasTarget ? "target" : "copyOnly"),
            Privacy: new PrivacyState(
                texts.Privacy(vm.CloudTranslate, vm.CloudClean), cloud, texts.PrivacyAria(vm.CloudTranslate, vm.CloudClean)),
            Output: new OutputState(
                Name(vm.Output),
                texts.OutputPill(vm.Output, vm.TargetLanguage),
                vm.Output == ComposerOutput.Translate,
                [.. vm.Outputs.Select(o => new MenuItemState(Name(o), texts.OutputMenu(o, vm.TargetLanguage), vm.Output == o, true))],
                texts.SwapMenu(vm.TargetLanguage),
                vm.RememberFor is { } rememberFor ? texts.RememberMenuNote(rememberFor) : null),
            Banner: BannerOf(vm.Banner, texts),
            Source: new SourceState(
                live ? "live" : "edit",
                vm.SourceText,
                sourceRev,
                vm.IsEditable,
                texts.SourceLabel(vm.IsListening || vm.IsStartingListening, vm.HasTimeline, vm.TranscriptName),
                vm.IsWholeRoute,
                empty,
                [.. vm.SourceLines.Select(line => new SourceLineState(line.Time, line.Text, line.IsTail))],
                empty && recent.Unsent is { } unsent && vm.CanReplaceDraft
                    ? new ResumeState(0, ComposerTexts.RecentPreview(unsent))
                    : null,
                empty && vm.Remembered is { } remembered
                    ? new RememberedState(texts.RememberedNote(remembered, vm.Output, vm.TargetLanguage), texts.RestoreDefaults)
                    : null,
                !vm.IsClearing && !vm.IsWritten && (vm.SourceText.Length > 0 || vm.HasTimeline || vm.IsListening || vm.IsStartingListening || vm.IsTranscribing)),
            Result: new ResultState(
                hasResult,
                vm.IsListening && vm.Output == ComposerOutput.Clean
                    ? texts.CleanupPreview
                    : texts.ResultLabel(vm.Output, vm.TargetLanguage),
                hasResult ? [.. vm.Lines.Select(line => Row(line, vm, texts))] : [],
                vm.EditText),
            Footer: new FooterState(
                new MicState(vm.IsListening, vm.IsListening ? texts.MicStop : texts.MicStart,
                    !vm.IsClearing),
                new StatusState(
                    texts.Status(vm.StatusInfo),
                    !vm.IsListening && (vm.Status == ComposerStatus.Failed ||
                        vm.Output == ComposerOutput.Clean && vm.StatusInfo.PolishOutcome == LiveLingo.Core.Drafting.PolishOutcome.KeptPreview)
                        ? "warn" : "muted"),
                vm.DownloadProgress is { } download && vm.Banner.Kind != BannerKind.Downloading
                    ? new DownloadState(texts.FooterDownload(download), (int)Math.Round(download))
                    : null,
                vm.CanCopy,
                new CommitState(
                    texts.CommitLabel(vm.IsWritten, armed, vm.HasTarget, targetName),
                    !vm.IsWritten && (armed || vm.CanWrite || vm.Status == ComposerStatus.Working),
                    armed,
                    vm.IsWritten,
                    texts.CommitAria(armed, vm.HasTarget))),
            Layout: new LayoutState(vm.IsWide),
            Options: new OptionsState(
                vm.Animations,
                vm.Theme,
                vm.CommitKey == CommitKey.Enter ? "enter" : "ctrlEnter",
                ComposerTexts.CommitKeyLabel(vm.CommitKey),
                ComposerTexts.NewlineKeyLabel,
                texts.ShortcutModifier,
                [.. ComposerShortcuts.All.Select(s => new ShortcutState(s.Id, s.Code, s.Shift, texts.Cap(s.Id)))]),
            Menus: new MenusState(
                [
                    new MenuItemState("target", texts.TargetMenuSome(targetName), vm.HasTarget, vm.HasOriginalTarget),
                    new MenuItemState("copyOnly", texts.TargetMenuNone, !vm.HasTarget, true)
                ],
                [
                    new MenuItemState("new", texts.MoreNew, false, true),
                    new MenuItemState("recent", texts.MoreRecent, false, true),
                    new MenuItemState("import", texts.MoreImport, false, vm.CanReplaceDraft),
                    new MenuItemState("export", texts.MoreExport, false, true),
                    new MenuItemState("wide", texts.MoreWide, vm.ForceWide, true),
                    new MenuItemState("whole", texts.MoreWhole, vm.ForceWhole, true),
                    new MenuItemState("settings", texts.MoreSettings, false, true)
                ]),
            Dialogs: new DialogsState(
                [.. recent.Items.Select((draft, i) => new RecentItemState(i, ComposerTexts.RecentItem(draft)))],
                vm.CanReplaceDraft,
                new ExportState(true, true, vm.HasTimeline, vm.LastExportFormat is { } last ? Name(last) : null)));
    }

    private static string Name(ExportFormat format) => format switch
    {
        ExportFormat.Txt => "txt",
        ExportFormat.Markdown => "markdown",
        _ => "srt"
    };

    private static string Name(ComposerOutput output) => output switch
    {
        ComposerOutput.Clean => "clean",
        ComposerOutput.Translate => "translate",
        _ => "original"
    };

    private static BannerView? BannerOf(BannerState banner, ComposerTexts texts) => banner.Kind == BannerKind.None
        ? null
        : new BannerView(
            char.ToLowerInvariant(banner.Kind.ToString()[0]) + banner.Kind.ToString()[1..],
            banner.Tone.ToString().ToLowerInvariant(),
            texts.BannerMessage(banner),
            banner.Progress,
            [.. banner.Actions.Select(a => new ActionState(
                char.ToLowerInvariant(a.ToString()[0]) + a.ToString()[1..], texts.BannerActionLabel(banner, a)))]);

    private static RowState Row(ResultLine line, ComposerViewModel vm, ComposerTexts texts) => new(
        line.Index,
        line.State.ToString().ToLowerInvariant(),
        texts.Line(line),
        texts.RowNote(line),
        line.Time,
        line.Rejection is not null,
        line.IsOwned,
        vm.EditingIndex == line.Index,
        vm.CanEditRows,
        vm.CanEditRows && vm.Output == ComposerOutput.Clean,
        texts.RowUseOriginalAria(line.Index),
        texts.RowEditAria(line.Index));
}
