using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Composer;

/// <summary>Turns view-model states into localized text, so the window only binds strings.</summary>
public sealed class ComposerTexts(ILocalizationService loc)
{
    public string Status(ComposerStatus status, bool writeQueued)
    {
        if (writeQueued)
            return loc.T("composer.status.queued");

        return loc.T(status switch
        {
            ComposerStatus.Empty => "composer.status.empty",
            ComposerStatus.Starting => "composer.status.starting",
            ComposerStatus.Listening => "composer.status.listening",
            ComposerStatus.Working => "composer.status.working",
            ComposerStatus.Ready => "composer.status.ready",
            ComposerStatus.Failed => "composer.status.failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        });
    }

    public string CleanLabel => loc.T("composer.output.clean");
    public string OriginalLabel => loc.T("composer.output.original");
    public string CopyLabel => loc.T("composer.copy");
    public string WriteLabel => loc.T("composer.write");
    public string RetryLabel => loc.T("composer.retry");
    public string Placeholder => loc.T("composer.placeholder");
    public string MicLabel => loc.T("composer.mic");

    public string TranslateLabel(TargetLanguage target) =>
        loc.T("composer.output.translate", loc.T("composer.language." + target));

    public string Target(string targetName, bool hasTarget) =>
        hasTarget ? loc.T("composer.target.some", targetName) : loc.T("composer.target.none");

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
        _ => throw new ArgumentOutOfRangeException(nameof(banner))
    };

    public string BannerAction(BannerState banner) => banner.Kind switch
    {
        BannerKind.ModelMissing => loc.T("composer.banner.download"),
        BannerKind.DownloadFailed => loc.T("composer.banner.downloadRetry"),
        BannerKind.MicrophoneDenied => loc.T("composer.banner.micDeniedAction"),
        _ => ""
    };

    public string Line(ResultLine line) => line.State switch
    {
        LineState.Failed => line.Error,
        LineState.Empty => loc.T("composer.result.empty"),
        LineState.Pending => "",
        _ => line.Text
    };
}
