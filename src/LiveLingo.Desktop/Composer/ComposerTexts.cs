using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Composer;

/// <summary>Turns view-model states into localized text, so the window only binds strings.</summary>
/// <param name="mac">Shortcut caps follow macOS conventions; defaults to the running operating system.</param>
public sealed class ComposerTexts(ILocalizationService loc, bool? mac = null)
{
    private bool IsMac => mac ?? OperatingSystem.IsMacOS();

    /// <summary>The key cap of a shortcut of <see cref="ComposerShortcuts"/>, e.g. "⌘D".</summary>
    public string Cap(string shortcutId) => ComposerShortcuts.Cap(ComposerShortcuts.Of(shortcutId), IsMac);

    /// <summary>Which modifier the shortcuts use: "meta" (Command) on macOS, "ctrl" elsewhere.</summary>
    public string ShortcutModifier => IsMac ? "meta" : "ctrl";

    // ---- header ----------------------------------------------------------------

    public string DestAria => loc.T("composer.dest.aria");
    public string OutputAria => loc.T("composer.out.aria");
    public string MoreAria => loc.T("composer.more");
    public string CloseAria => loc.T("composer.close");
    public string ZoomAria => loc.T("composer.zoom");
    public string UnzoomAria => loc.T("composer.unzoom");

    /// <summary>The destination chip: the app that receives the text, a lost window, or copy only.</summary>
    public string Dest(bool hasTarget, bool targetLost, string targetName) =>
        targetLost ? loc.T("composer.dest.lost") : hasTarget ? targetName : loc.T("composer.dest.copyOnly");

    public string TargetMenuSome(string targetName) => loc.T("composer.menu.target.some", targetName);
    public string TargetMenuNone => loc.T("composer.menu.target.none");

    public string Privacy(bool cloudTranslate, bool cloudClean)
    {
        if (!cloudTranslate && !cloudClean)
            return loc.T("composer.privacy.local");
        return loc.T("composer.privacy.cloud", CloudSteps(cloudTranslate, cloudClean));
    }

    public string PrivacyAria(bool cloudTranslate, bool cloudClean) =>
        !cloudTranslate && !cloudClean
            ? loc.T("composer.privacy.aria.local")
            : loc.T("composer.privacy.aria.cloud", CloudSteps(cloudTranslate, cloudClean));

    private string CloudSteps(bool translate, bool clean) => string.Join(
        loc.T("composer.privacy.sep"),
        new[] { translate ? loc.T("composer.privacy.translate") : null, clean ? loc.T("composer.privacy.clean") : null }
            .Where(step => step is not null));

    /// <summary>English stays "English" and Chinese stays "中文" in every interface language, as in the prototype.</summary>
    public string LanguageName(TargetLanguage language) => loc.T("composer.language." + language);

    public static TargetLanguage SourceLanguage(TargetLanguage target) =>
        target == TargetLanguage.English ? TargetLanguage.Chinese : TargetLanguage.English;

    public string OutputPill(ComposerOutput output, TargetLanguage target) => output switch
    {
        ComposerOutput.Clean => loc.T("composer.out.clean", LanguageName(SourceLanguage(target))),
        ComposerOutput.Translate => loc.T("composer.out.translate", LanguageName(target)),
        _ => loc.T("composer.out.raw")
    };

    public string OutputMenu(ComposerOutput output, TargetLanguage target) => output switch
    {
        ComposerOutput.Clean => loc.T("composer.out.menu.clean", LanguageName(SourceLanguage(target))),
        ComposerOutput.Translate => loc.T("composer.out.menu.translate", LanguageName(target)),
        _ => loc.T("composer.out.menu.raw")
    };

    public string SwapMenu(TargetLanguage target) =>
        loc.T("composer.out.swap", LanguageName(SourceLanguage(target)), LanguageName(target));

    public string MoreNew => loc.T("composer.menu.new");
    public string MoreRecent => loc.T("composer.menu.recent");
    public string MoreImport => loc.T("composer.menu.import");
    public string MoreExport => loc.T("composer.menu.export");
    public string MoreWide => loc.T("composer.menu.wide");
    public string MoreWhole => loc.T("composer.menu.whole");
    public string MoreSettings => loc.T("composer.menu.settings");
    public string RecentClear => loc.T("composer.recent.clear");
    public string RecentEmpty => loc.T("composer.recent.empty");
    public string RecentRestore => loc.T("composer.recent.restore");
    public string Resume => loc.T("composer.resume");
    public string RestoreDefaults => loc.T("composer.remember.restore");

    /// <summary>The application's name; the context without an input box (copy only) has none and is called by what it does.</summary>
    public string ContextName(string name) => name.Length > 0 ? name : loc.T("composer.remember.clipboard");

    /// <summary>"Using your last choice for Slack: translate to English", shown while a remembered choice stands.</summary>
    public string RememberedNote(RememberContext context, ComposerOutput output, TargetLanguage target) =>
        loc.T("composer.remember.note", ContextName(context.Name), OutputMenu(output, target));

    /// <summary>The line of the output menu that says whose choices are remembered.</summary>
    public string RememberMenuNote(RememberContext context) => loc.T("composer.remember.menu", ContextName(context.Name));

    /// <summary>The first words of a draft's first line, to recognize it by.</summary>
    public static string RecentPreview(RecentDraft draft)
    {
        var firstLine = draft.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return firstLine.Length > 28 ? firstLine[..28] + "…" : firstLine;
    }

    /// <summary>"12:03 · first words of the draft" for the recent-drafts menu.</summary>
    public static string RecentItem(RecentDraft draft) => $"{draft.At.ToLocalTime():HH:mm} · {RecentPreview(draft)}";

    // ---- source pane -------------------------------------------------------------

    public string Placeholder => loc.T("composer.placeholder");
    public string RouteChip => loc.T("composer.route.whole");
    public string ResultAria => loc.T("composer.res.aria");

    public string SourceLabel(bool listening, bool timeline, string fileName) =>
        timeline ? loc.T("composer.src.transcript", fileName) : listening ? loc.T("composer.src.listening") : loc.T("composer.src.label");

    public string ResultLabel(ComposerOutput output, TargetLanguage target) =>
        output == ComposerOutput.Translate ? loc.T("composer.res.labelTo", LanguageName(target)) : loc.T("composer.res.label");

    public string HintMic => loc.T("composer.hint.mic", Cap("dictate"));
    public string HintWrite => loc.T("composer.hint.write");
    public string HintNewline => loc.T("composer.hint.newline");
    public string HintHide => loc.T("composer.hint.hide");

    /// <summary>The key cap of the commit key: "↵", or "⌃↵" on macOS and "Ctrl+↵" elsewhere for Ctrl+Enter.</summary>
    public static string CommitKeyLabel(CommitKey key) => key == CommitKey.Enter ? "↵" : OperatingSystem.IsMacOS() ? "⌃↵" : "Ctrl+↵";

    public static string NewlineKeyLabel => OperatingSystem.IsMacOS() ? "⇧↵" : "Shift+↵";

    // ---- result rows -------------------------------------------------------------

    public string RowRetry => loc.T("composer.row.retry");
    public string RowManual => loc.T("composer.row.manual");
    public string RowSave => loc.T("composer.row.save");
    public string RowCancel => loc.T("composer.row.cancel");
    public string RowProcessing => loc.T("composer.row.processing");
    public string RowUseOriginal => loc.T("composer.row.useOriginal");
    public string RowEdit => loc.T("composer.row.edit");
    public string RowUseOriginalAria(int index) => loc.T("composer.row.useOriginal.aria", index + 1);
    public string RowEditAria(int index) => loc.T("composer.row.edit.aria", index + 1);

    public string Line(ResultLine line) => line.State switch
    {
        LineState.Failed => line.Error,
        LineState.Empty => loc.T("composer.row.empty"),
        LineState.Pending => "",
        _ => line.Text
    };

    /// <summary>The note under a sentence: why the cleanup was not used, or that the user edited it.</summary>
    public string RowNote(ResultLine line) =>
        line.Rejection is { } rejection ? loc.T("composer.row.kept", loc.T($"composer.gate.{rejection.Reason}", rejection.Detail))
        : line.IsOwned ? loc.T("composer.row.edited") : "";

    // ---- footer ------------------------------------------------------------------

    public string MicStart => loc.T("composer.mic.start", Cap("dictate"));
    public string MicStop => loc.T("composer.mic.stop", Cap("dictate"));
    public string CopyAria => loc.T("composer.copy.aria");
    public string ModelDetail(ModelStage stage, ModelUse? use, ModelUse? previous)
    {
        var label = loc.T($"composer.model.{stage}");
        if (use is null)
            return label + ": " + loc.T("composer.model.unused");
        var detail = use switch
        {
            { Cloud: true, Confirmed: false } => loc.T("composer.model.requested", use.Name),
            { Cloud: true } => loc.T("composer.model.cloud", use.Name),
            { Confirmed: true } => loc.T("composer.model.local", use.Name),
            _ when previous is not null => loc.T("composer.model.switching", use.Name, previous.Name),
            _ => loc.T("composer.model.preparing", use.Name)
        };
        return label + ": " + detail;
    }
    public string CleanupPreview => loc.T("composer.result.preview");

    public string Status(StatusInfo info)
    {
        if (info.Written)
            return loc.T("composer.status.written");
        if (info.Status == ComposerStatus.Starting)
            return loc.T("composer.status.starting");
        if (info.Status == ComposerStatus.Listening)
            return loc.T("composer.status.listening", Clock(info.RecordingSeconds), Cap("dictate"));
        if (info.Status == ComposerStatus.Transcribing)
            return loc.T("composer.status.transcribing", info.TranscribePercent);
        if (info.Status == ComposerStatus.Clearing)
            return loc.T("composer.status.clearing");
        if (info.Polishing)
            return loc.T("composer.status.polishing");
        if (info.Armed)
            return loc.T("composer.status.armed");
        if (info.Status == ComposerStatus.Ready && info.Output == ComposerOutput.Clean)
        {
            if (info.PolishOutcome == PolishOutcome.KeptPreview)
                return loc.T("composer.status.polish.kept");
            if (info.PolishOutcome == PolishOutcome.DefaultRequirements)
                return loc.T("composer.status.polish.default");
        }

        return info.Status switch
        {
            ComposerStatus.Empty => "",
            ComposerStatus.Preparing => loc.T("composer.status.preparing"),
            ComposerStatus.Working => loc.T(info.Output == ComposerOutput.Translate ? "composer.status.working.translate" : "composer.status.working.clean"),
            ComposerStatus.Failed => loc.T("composer.status.unavailable"),
            ComposerStatus.Ready => loc.T(info.UsesCloud ? "composer.status.ready.cloud" : "composer.status.ready.local", info.Units),
            _ => throw new ArgumentOutOfRangeException(nameof(info))
        };
    }

    public static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>The primary button: "写入 Agent", "复制" without an input box, "完成后写入" while armed, "已写入" afterwards.</summary>
    public string CommitLabel(bool written, bool armed, bool hasTarget, string targetName) =>
        armed ? loc.T("composer.commit.armed")
        : written ? loc.T("composer.commit.written")
        : hasTarget ? loc.T("composer.commit.write", targetName)
        : loc.T("composer.commit.copy");

    public string CommitAria(bool armed, bool hasTarget) =>
        armed ? loc.T("composer.commit.armed.aria") : hasTarget ? loc.T("composer.commit.write.aria") : loc.T("composer.commit.copy.aria");

    // ---- banners -----------------------------------------------------------------

    /// <summary>The footer's line for models that are downloading while the banner is out of the way.</summary>
    public string FooterDownload(double percent) => loc.T("composer.footer.download", Math.Round(percent));

    public string BannerMessage(BannerState banner) => banner.Kind switch
    {
        BannerKind.None => "",
        BannerKind.ModelMissing => loc.T("composer.banner.modelMissing", banner.Detail),
        BannerKind.Downloading => loc.T("composer.banner.downloading", Math.Round(banner.Progress)),
        BannerKind.DownloadFailed => loc.T("composer.banner.downloadFailed", banner.Detail),
        BannerKind.TargetLost => loc.T("composer.banner.targetLost"),
        BannerKind.WriteFailed => loc.T("composer.banner.writeFailed", banner.Detail),
        BannerKind.MicrophoneDenied => loc.T("composer.banner.micDenied"),
        BannerKind.NoMicrophone => loc.T("composer.banner.noMic", banner.Detail),
        BannerKind.NoSound => loc.T("composer.banner.noSound"),
        BannerKind.FileFailed => loc.T("composer.banner.fileFailed", banner.Detail),
        BannerKind.NoSpeechFound => loc.T("composer.banner.noSpeech"),
        BannerKind.SlowProcessing => loc.T("composer.banner.slow"),
        _ => throw new ArgumentOutOfRangeException(nameof(banner))
    };

    public string BannerActionLabel(BannerState banner, BannerAction action) => action switch
    {
        BannerAction.Dismiss => loc.T("composer.banner.dismiss"),
        BannerAction.Background => loc.T("composer.banner.background"),
        BannerAction.Download => loc.T(banner.Kind == BannerKind.DownloadFailed ? "composer.banner.retry" : "composer.banner.download"),
        BannerAction.OpenMicrophoneSettings => loc.T("composer.banner.openOs"),
        BannerAction.OpenInputSettings => loc.T("composer.banner.checkInput"),
        BannerAction.UseOriginal => loc.T("composer.banner.useOriginal"),
        BannerAction.KeepWaiting => loc.T("composer.banner.keepWaiting"),
        BannerAction.Retarget => loc.T("composer.banner.retarget"),
        BannerAction.Copy => loc.T("composer.banner.copy"),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public string BannerProgressAria => loc.T("composer.banner.progress.aria");

    // ---- notices and dialogs -------------------------------------------------------

    public string Toast(ToastKind kind) => loc.T(kind switch
    {
        ToastKind.Copied => "composer.toast.copied",
        ToastKind.Cancelled => "composer.toast.cancelled",
        ToastKind.Empty => "composer.toast.empty",
        ToastKind.Unavailable => "composer.toast.unavailable",
        ToastKind.Processing => "composer.toast.processing",
        ToastKind.Exported => "composer.toast.exported",
        ToastKind.ExportUnfinished => "composer.toast.exportUnfinished",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    /// <summary>The interface language, for the page's lang attribute.</summary>
    public string Locale => loc.CurrentCulture.Name;

    public string ImportTitle => loc.T("composer.import.title");
    public string ImportFileType => loc.T("composer.import.fileType");
    public string ExportTitle => loc.T("composer.export.title");
    public string ExportHint => loc.T("composer.export.hint");

    /// <summary>
    /// The labels that do not depend on state, sent once per page load and language change. Text that depends on state
    /// (status, banners, row notes, menus) arrives inside each snapshot instead.
    /// </summary>
    public IReadOnlyDictionary<string, string> WebStrings() => new Dictionary<string, string>
    {
        ["placeholder"] = Placeholder,
        ["hintMic"] = HintMic,
        ["hintWrite"] = HintWrite,
        ["hintNewline"] = HintNewline,
        ["hintHide"] = HintHide,
        ["destAria"] = DestAria,
        ["outputAria"] = OutputAria,
        ["moreAria"] = MoreAria,
        ["closeAria"] = CloseAria,
        ["zoomAria"] = ZoomAria,
        ["unzoomAria"] = UnzoomAria,
        ["copyAria"] = CopyAria,
        ["clear"] = loc.T("composer.clear"),
        ["clearAria"] = loc.T("composer.clear.aria", Cap("new")),
        ["modelInfo"] = loc.T("composer.model.info"),
        ["modelCloud"] = loc.T("composer.model.cloud.badge"),
        ["modelCloudAria"] = loc.T("composer.model.cloud.aria"),
        ["resultAria"] = ResultAria,
        ["routeChip"] = RouteChip,
        ["rowRetry"] = RowRetry,
        ["rowManual"] = RowManual,
        ["rowSave"] = RowSave,
        ["rowCancel"] = RowCancel,
        ["rowProcessing"] = RowProcessing,
        ["rowUseOriginal"] = RowUseOriginal,
        ["rowEdit"] = RowEdit,
        ["progressAria"] = BannerProgressAria,
        ["recentTitle"] = MoreRecent,
        ["recentClear"] = RecentClear,
        ["recentEmpty"] = RecentEmpty,
        ["recentRestore"] = RecentRestore,
        ["resume"] = Resume,
        ["restoreDefaults"] = RestoreDefaults,
        ["exportTitle"] = MoreExport,
        ["exportHint"] = ExportHint
    };
}
