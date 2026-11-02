using LiveLingo.Core.Drafting;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerTextsTests
{
    private static ComposerTexts Create(bool mac = true)
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        loc.T(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(call => "[" + call.ArgAt<string>(0) + ":" + string.Join(",", call.ArgAt<object[]>(1)) + "]");
        return new ComposerTexts(loc, mac);
    }

    private static StatusInfo Info(
        ComposerStatus status = ComposerStatus.Empty, bool written = false, int seconds = 0,
        bool armed = false, int units = 0, ComposerOutput output = ComposerOutput.Translate, bool cloud = false, int percent = 0) =>
        new(status, written, seconds, armed, units, output, cloud, percent);

    [Theory]
    [InlineData(ComposerStatus.Empty, "")]
    [InlineData(ComposerStatus.Starting, "[composer.status.starting]")]
    [InlineData(ComposerStatus.Preparing, "[composer.status.preparing]")]
    [InlineData(ComposerStatus.Failed, "[composer.status.unavailable]")]
    public void Status_IsLocalized(ComposerStatus status, string expected)
    {
        Assert.Equal(expected, Create().Status(Info(status)));
    }

    [Fact]
    public void Status_Listening_ShowsTheClockAndTheShortcutThatStopsIt()
    {
        Assert.Equal("[composer.status.listening:1:05,⌘D]", Create().Status(Info(ComposerStatus.Listening, seconds: 65)));
        Assert.Equal("[composer.status.listening:0:07,Ctrl+D]", Create(mac: false).Status(Info(ComposerStatus.Listening, seconds: 7)));
    }

    [Fact]
    public void TheMicrophoneTexts_NameTheShortcut()
    {
        var texts = Create();

        Assert.Equal("[composer.mic.start:⌘D]", texts.MicStart);
        Assert.Equal("[composer.mic.stop:⌘D]", texts.MicStop);
        Assert.Equal("[composer.hint.mic:⌘D]", texts.HintMic);
        Assert.Equal("⇧⌘C", texts.Cap("copy"));
        Assert.Equal("meta", texts.ShortcutModifier);
        Assert.Equal("ctrl", Create(mac: false).ShortcutModifier);
    }

    [Fact]
    public void Status_Working_NamesTheStep()
    {
        var texts = Create();

        Assert.Equal("[composer.status.working.translate]", texts.Status(Info(ComposerStatus.Working, output: ComposerOutput.Translate)));
        Assert.Equal("[composer.status.working.clean]", texts.Status(Info(ComposerStatus.Working, output: ComposerOutput.Clean)));
    }

    [Fact]
    public void Status_Ready_SaysWhereTheTextWasProduced()
    {
        var texts = Create();

        Assert.Equal("[composer.status.ready.local:3]", texts.Status(Info(ComposerStatus.Ready, units: 3)));
        Assert.Equal("[composer.status.ready.cloud:3]", texts.Status(Info(ComposerStatus.Ready, units: 3, cloud: true)));
    }

    [Fact]
    public void Status_WrittenAndArmedAndTranscribing_TakePrecedence()
    {
        var texts = Create();

        Assert.Equal("[composer.status.written]", texts.Status(Info(ComposerStatus.Ready, written: true, armed: true)));
        Assert.Equal("[composer.status.armed]", texts.Status(Info(ComposerStatus.Working, armed: true)));
        Assert.Equal("[composer.status.transcribing:42]", texts.Status(Info(ComposerStatus.Transcribing, percent: 42)));
    }

    [Fact]
    public void Status_RejectsAnUnknownStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create().Status(Info((ComposerStatus)99)));
    }

    [Fact]
    public void Dest_DistinguishesAnInputFromCopyOnlyAndALostWindow()
    {
        var texts = Create();

        Assert.Equal("Slack", texts.Dest(hasTarget: true, targetLost: false, "Slack"));
        Assert.Equal("[composer.dest.copyOnly]", texts.Dest(hasTarget: false, targetLost: false, ""));
        Assert.Equal("[composer.dest.lost]", texts.Dest(hasTarget: true, targetLost: true, "Slack"));
    }

    [Fact]
    public void Privacy_NamesEveryCloudStep()
    {
        var texts = Create();

        Assert.Equal("[composer.privacy.local]", texts.Privacy(false, false));
        Assert.Equal("[composer.privacy.cloud:[composer.privacy.translate]]", texts.Privacy(true, false));
        Assert.Equal("[composer.privacy.cloud:[composer.privacy.clean]]", texts.Privacy(false, true));
        Assert.Equal(
            "[composer.privacy.cloud:[composer.privacy.translate][composer.privacy.sep][composer.privacy.clean]]",
            texts.Privacy(true, true));
        Assert.Equal("[composer.privacy.aria.local]", texts.PrivacyAria(false, false));
        Assert.Equal("[composer.privacy.aria.cloud:[composer.privacy.clean]]", texts.PrivacyAria(false, true));
    }

    [Fact]
    public void OutputPillAndMenu_NameTheLanguages()
    {
        var texts = Create();

        Assert.Equal("[composer.out.translate:[composer.language.English]]", texts.OutputPill(ComposerOutput.Translate, TargetLanguage.English));
        Assert.Equal("[composer.out.clean:[composer.language.English]]", texts.OutputPill(ComposerOutput.Clean, TargetLanguage.Chinese));
        Assert.Equal("[composer.out.raw]", texts.OutputPill(ComposerOutput.Original, TargetLanguage.English));
        Assert.Equal("[composer.out.menu.translate:[composer.language.Chinese]]", texts.OutputMenu(ComposerOutput.Translate, TargetLanguage.Chinese));
        Assert.Equal("[composer.out.menu.clean:[composer.language.Chinese]]", texts.OutputMenu(ComposerOutput.Clean, TargetLanguage.English));
        Assert.Equal("[composer.out.menu.raw]", texts.OutputMenu(ComposerOutput.Original, TargetLanguage.English));
        Assert.Equal(
            "[composer.out.swap:[composer.language.Chinese],[composer.language.English]]",
            texts.SwapMenu(TargetLanguage.English));
    }

    [Fact]
    public void SourceLanguage_IsTheOtherSideOfTheTarget()
    {
        Assert.Equal(TargetLanguage.Chinese, ComposerTexts.SourceLanguage(TargetLanguage.English));
        Assert.Equal(TargetLanguage.English, ComposerTexts.SourceLanguage(TargetLanguage.Chinese));
    }

    [Fact]
    public void Labels_FollowTheComposerState()
    {
        var texts = Create();

        Assert.Equal("[composer.src.transcript:a.wav]", texts.SourceLabel(false, true, "a.wav"));
        Assert.Equal("[composer.src.listening]", texts.SourceLabel(true, false, ""));
        Assert.Equal("[composer.src.label]", texts.SourceLabel(false, false, ""));
        Assert.Equal("[composer.res.labelTo:[composer.language.English]]", texts.ResultLabel(ComposerOutput.Translate, TargetLanguage.English));
        Assert.Equal("[composer.res.label]", texts.ResultLabel(ComposerOutput.Clean, TargetLanguage.English));
    }

    [Fact]
    public void CommitLabel_FollowsTheArmedWrittenAndTargetState()
    {
        var texts = Create();

        Assert.Equal("[composer.commit.armed]", texts.CommitLabel(written: false, armed: true, hasTarget: true, "Slack"));
        Assert.Equal("[composer.commit.written]", texts.CommitLabel(written: true, armed: false, hasTarget: true, "Slack"));
        Assert.Equal("[composer.commit.write:Slack]", texts.CommitLabel(false, false, true, "Slack"));
        Assert.Equal("[composer.commit.copy]", texts.CommitLabel(false, false, false, ""));
        Assert.Equal("[composer.commit.armed.aria]", texts.CommitAria(armed: true, hasTarget: true));
        Assert.Equal("[composer.commit.write.aria]", texts.CommitAria(false, true));
        Assert.Equal("[composer.commit.copy.aria]", texts.CommitAria(false, false));
    }

    [Fact]
    public void KeyCaps_ShowTheCommitAndNewlineKeys()
    {
        Assert.Equal("↵", ComposerTexts.CommitKeyLabel(CommitKey.Enter));
        Assert.Contains("↵", ComposerTexts.CommitKeyLabel(CommitKey.CtrlEnter));
        Assert.NotEqual("↵", ComposerTexts.CommitKeyLabel(CommitKey.CtrlEnter));
        Assert.Contains("↵", ComposerTexts.NewlineKeyLabel);
    }

    [Fact]
    public void Clock_PadsTheSeconds()
    {
        Assert.Equal("0:00", ComposerTexts.Clock(0));
        Assert.Equal("1:05", ComposerTexts.Clock(65));
        Assert.Equal("12:30", ComposerTexts.Clock(750));
    }

    [Theory]
    [InlineData(BannerKind.ModelMissing, "x", 0, "[composer.banner.modelMissing:x]")]
    [InlineData(BannerKind.Downloading, "", 42.4, "[composer.banner.downloading:42]")]
    [InlineData(BannerKind.DownloadFailed, "disk full", 0, "[composer.banner.downloadFailed:disk full]")]
    [InlineData(BannerKind.TargetLost, "", 0, "[composer.banner.targetLost]")]
    [InlineData(BannerKind.WriteFailed, "no focus", 0, "[composer.banner.writeFailed:no focus]")]
    [InlineData(BannerKind.MicrophoneDenied, "", 0, "[composer.banner.micDenied]")]
    [InlineData(BannerKind.NoMicrophone, "no device", 0, "[composer.banner.noMic:no device]")]
    [InlineData(BannerKind.NoSound, "", 0, "[composer.banner.noSound]")]
    [InlineData(BannerKind.FileFailed, "bad file", 0, "[composer.banner.fileFailed:bad file]")]
    [InlineData(BannerKind.NoSpeechFound, "", 0, "[composer.banner.noSpeech]")]
    [InlineData(BannerKind.SlowProcessing, "", 0, "[composer.banner.slow]")]
    [InlineData(BannerKind.None, "", 0, "")]
    public void BannerMessage_IsLocalized(BannerKind kind, string detail, double progress, string message)
    {
        Assert.Equal(message, Create().BannerMessage(new BannerState(kind, detail, progress)));
    }

    [Theory]
    [InlineData(BannerKind.ModelMissing, BannerTone.Info, new[] { BannerAction.Download })]
    [InlineData(BannerKind.Downloading, BannerTone.Info, new[] { BannerAction.Dismiss })]
    [InlineData(BannerKind.DownloadFailed, BannerTone.Bad, new[] { BannerAction.Download })]
    [InlineData(BannerKind.TargetLost, BannerTone.Bad, new[] { BannerAction.Retarget, BannerAction.Copy })]
    [InlineData(BannerKind.MicrophoneDenied, BannerTone.Warn, new[] { BannerAction.OpenMicrophoneSettings, BannerAction.Dismiss })]
    [InlineData(BannerKind.NoSound, BannerTone.Warn, new[] { BannerAction.OpenInputSettings, BannerAction.Dismiss })]
    [InlineData(BannerKind.SlowProcessing, BannerTone.Info, new[] { BannerAction.UseOriginal, BannerAction.KeepWaiting })]
    [InlineData(BannerKind.WriteFailed, BannerTone.Bad, new BannerAction[0])]
    public void Banner_HasAToneAndTheActionsOfThePrototype(BannerKind kind, BannerTone tone, BannerAction[] actions)
    {
        var banner = new BannerState(kind);

        Assert.Equal(tone, banner.Tone);
        Assert.Equal(actions, banner.Actions);
    }

    [Theory]
    [InlineData(BannerAction.Dismiss, BannerKind.NoSound, "[composer.banner.dismiss]")]
    [InlineData(BannerAction.Dismiss, BannerKind.Downloading, "[composer.banner.background]")]
    [InlineData(BannerAction.Download, BannerKind.ModelMissing, "[composer.banner.download]")]
    [InlineData(BannerAction.Download, BannerKind.DownloadFailed, "[composer.banner.retry]")]
    [InlineData(BannerAction.OpenMicrophoneSettings, BannerKind.MicrophoneDenied, "[composer.banner.openOs]")]
    [InlineData(BannerAction.OpenInputSettings, BannerKind.NoSound, "[composer.banner.checkInput]")]
    [InlineData(BannerAction.UseOriginal, BannerKind.SlowProcessing, "[composer.banner.useOriginal]")]
    [InlineData(BannerAction.KeepWaiting, BannerKind.SlowProcessing, "[composer.banner.keepWaiting]")]
    [InlineData(BannerAction.Retarget, BannerKind.TargetLost, "[composer.banner.retarget]")]
    [InlineData(BannerAction.Copy, BannerKind.TargetLost, "[composer.banner.copy]")]
    public void BannerActionLabel_IsLocalized(BannerAction action, BannerKind kind, string label)
    {
        Assert.Equal(label, Create().BannerActionLabel(new BannerState(kind), action));
    }

    [Fact]
    public void Line_ShowsTheErrorOrAnEmptyMarker()
    {
        var texts = Create();

        Assert.Equal("hello", texts.Line(new ResultLine(0, "hello", LineState.Ready, "")));
        Assert.Equal("boom", texts.Line(new ResultLine(0, "", LineState.Failed, "boom")));
        Assert.Equal("[composer.row.empty]", texts.Line(new ResultLine(0, "", LineState.Empty, "")));
        Assert.Equal("", texts.Line(new ResultLine(0, "", LineState.Pending, "")));
    }

    [Fact]
    public void RowNote_ExplainsAKeptOriginalOrAnEdit()
    {
        var texts = Create();

        Assert.Equal(
            "[composer.row.kept:[composer.gate.NumberLost:8080]]",
            texts.RowNote(new ResultLine(0, "a", LineState.Ready, "", Rejection: new GateRejection(GateReason.NumberLost, "8080"))));
        Assert.Equal("[composer.row.edited]", texts.RowNote(new ResultLine(0, "a", LineState.Ready, "", IsOwned: true)));
        Assert.Equal("", texts.RowNote(new ResultLine(0, "a", LineState.Ready, "")));
        Assert.Equal("[composer.row.edit.aria:2]", texts.RowEditAria(1));
        Assert.Equal("[composer.row.useOriginal.aria:2]", texts.RowUseOriginalAria(1));
    }

    [Theory]
    [InlineData(ToastKind.Copied, "[composer.toast.copied]")]
    [InlineData(ToastKind.Cancelled, "[composer.toast.cancelled]")]
    [InlineData(ToastKind.Empty, "[composer.toast.empty]")]
    [InlineData(ToastKind.Unavailable, "[composer.toast.unavailable]")]
    [InlineData(ToastKind.Processing, "[composer.toast.processing]")]
    [InlineData(ToastKind.Exported, "[composer.toast.exported]")]
    [InlineData(ToastKind.ExportUnfinished, "[composer.toast.exportUnfinished]")]
    public void Toast_IsLocalized(ToastKind kind, string expected)
    {
        Assert.Equal(expected, Create().Toast(kind));
    }

    [Fact]
    public void RecentItem_ShowsTheTimeAndTheFirstWordsOfTheFirstLine()
    {
        var at = new DateTimeOffset(2026, 10, 31, 4, 5, 0, TimeSpan.Zero);
        var local = at.ToLocalTime().ToString("HH:mm");

        Assert.Equal(local + " · hello", ComposerTexts.RecentItem(new RecentDraft(at, "\n  hello  \nsecond", false)));
        Assert.Equal(local + " · " + new string('a', 28) + "…", ComposerTexts.RecentItem(new RecentDraft(at, new string('a', 40), true)));
    }
}
