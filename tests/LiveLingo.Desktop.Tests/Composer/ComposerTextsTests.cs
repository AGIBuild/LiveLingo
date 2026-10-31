using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.Composer;

public class ComposerTextsTests
{
    private static ComposerTexts Create()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.T(Arg.Any<string>()).Returns(call => "[" + call.Arg<string>() + "]");
        loc.T(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(call => "[" + call.ArgAt<string>(0) + ":" + string.Join(",", call.ArgAt<object[]>(1)) + "]");
        return new ComposerTexts(loc);
    }

    [Theory]
    [InlineData(ComposerStatus.Empty, false, "[composer.status.empty]")]
    [InlineData(ComposerStatus.Working, false, "[composer.status.working]")]
    [InlineData(ComposerStatus.Ready, false, "[composer.status.ready]")]
    [InlineData(ComposerStatus.Failed, false, "[composer.status.failed]")]
    [InlineData(ComposerStatus.Working, true, "[composer.status.queued]")]
    public void Status_IsLocalized_AndQueuedWins(ComposerStatus status, bool queued, string expected)
    {
        Assert.Equal(expected, Create().Status(status, queued));
    }

    [Fact]
    public void TranslateLabel_NamesTheTargetLanguage()
    {
        Assert.Equal("[composer.output.translate:[composer.language.English]]", Create().TranslateLabel(TargetLanguage.English));
        Assert.Equal("[composer.output.translate:[composer.language.Chinese]]", Create().TranslateLabel(TargetLanguage.Chinese));
    }

    [Fact]
    public void Target_DistinguishesAnInputFromTheClipboard()
    {
        Assert.Equal("[composer.target.some:Slack]", Create().Target("Slack", hasTarget: true));
        Assert.Equal("[composer.target.none]", Create().Target("", hasTarget: false));
    }

    [Theory]
    [InlineData(BannerKind.ModelMissing, "x", 0, "[composer.banner.modelMissing:x]", "[composer.banner.download]")]
    [InlineData(BannerKind.Downloading, "", 42.4, "[composer.banner.downloading:42]", "")]
    [InlineData(BannerKind.DownloadFailed, "disk full", 0, "[composer.banner.downloadFailed:disk full]", "[composer.banner.downloadRetry]")]
    [InlineData(BannerKind.TargetLost, "", 0, "[composer.banner.targetLost]", "")]
    [InlineData(BannerKind.WriteFailed, "no focus", 0, "[composer.banner.writeFailed:no focus]", "")]
    public void Banner_ProvidesMessageAndAction(BannerKind kind, string detail, double progress, string message, string action)
    {
        var texts = Create();
        var banner = new BannerState(kind, detail, progress);

        Assert.Equal(message, texts.BannerMessage(banner));
        Assert.Equal(action, texts.BannerAction(banner));
    }

    [Fact]
    public void NoBanner_HasNoText()
    {
        var texts = Create();

        Assert.Equal("", texts.BannerMessage(BannerState.None));
        Assert.Equal("", texts.BannerAction(BannerState.None));
    }

    [Fact]
    public void ResultLine_ShowsTheErrorOrAnEmptyMarker()
    {
        var texts = Create();

        Assert.Equal("hello", texts.Line(new ResultLine(0, "hello", LineState.Ready, "")));
        Assert.Equal("boom", texts.Line(new ResultLine(0, "", LineState.Failed, "boom")));
        Assert.Equal("[composer.result.empty]", texts.Line(new ResultLine(0, "", LineState.Empty, "")));
        Assert.Equal("", texts.Line(new ResultLine(0, "", LineState.Pending, "")));
    }
}
