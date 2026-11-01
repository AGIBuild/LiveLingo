using LiveLingo.Core.Drafting;

namespace LiveLingo.Desktop.Composer;

/// <summary>The output pill: what is written into the target.</summary>
public enum ComposerOutput
{
    /// <summary>The cleaned-up text: fillers, repetitions and homophone errors fixed, meaning untouched.</summary>
    Clean,
    Translate,
    Original
}

public enum ComposerStatus
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>The microphone was pressed and the speech models are loading.</summary>
    Starting,

    /// <summary>The microphone is open and speech is being recognized.</summary>
    Listening,

    /// <summary>The recording is being transcribed; the progress is on the view model.</summary>
    Transcribing,

    /// <summary>The selected output is still being produced.</summary>
    Working,

    /// <summary>The result can be written or copied.</summary>
    Ready,

    /// <summary>A sentence failed or is empty; it can be retried or edited.</summary>
    Failed
}

public enum LineState
{
    Pending,
    Ready,
    Failed,
    Empty
}

/// <summary>One sentence of the "will be written" pane.</summary>
/// <param name="Time">Where the line starts in an imported recording ("1:05"); empty for typed or spoken text.</param>
/// <param name="Rejection">Why a cleanup was rejected and the original wording was kept; null otherwise.</param>
/// <param name="IsOwned">The user edited this sentence; model results never replace it.</param>
public sealed record ResultLine(
    int Index, string Text, LineState State, string Error, string Time = "", GateRejection? Rejection = null, bool IsOwned = false);

/// <summary>One line of the source pane while listening or showing a transcript.</summary>
/// <param name="IsTail">The sentence still being spoken (not settled yet).</param>
public sealed record SourceLine(string Time, string Text, bool IsTail);

public enum BannerKind
{
    None,

    /// <summary>The runtime or translation model is not installed; the banner asks for download consent.</summary>
    ModelMissing,
    Downloading,
    DownloadFailed,

    /// <summary>The window the text was meant for is gone; copy still works.</summary>
    TargetLost,

    /// <summary>Writing into the target failed; the result can still be copied.</summary>
    WriteFailed,

    /// <summary>The system denied microphone access; the banner offers to open the settings.</summary>
    MicrophoneDenied,

    /// <summary>There is no input device or it could not be opened.</summary>
    NoMicrophone,

    /// <summary>The microphone was open but only silence arrived; listening was stopped.</summary>
    NoSound,

    /// <summary>A recording could not be read, or subtitles could not be saved (unsupported format, unreadable or unwritable file).</summary>
    FileFailed,

    /// <summary>The recording was read but contains no recognizable speech.</summary>
    NoSpeechFound,

    /// <summary>The selected output takes much longer than usual; the user may fall back to the original words.</summary>
    SlowProcessing
}

public enum BannerTone
{
    Info,
    Warn,
    Bad
}

/// <summary>What a banner button does.</summary>
public enum BannerAction
{
    Dismiss,
    Download,
    OpenMicrophoneSettings,
    OpenInputSettings,
    UseOriginal,
    KeepWaiting,
    Retarget,
    Copy
}

/// <param name="Detail">Size, error text or other detail for the view to show next to the localized message.</param>
/// <param name="Progress">0–100 while downloading.</param>
public sealed record BannerState(BannerKind Kind, string Detail = "", double Progress = 0)
{
    public static BannerState None { get; } = new(BannerKind.None);

    public BannerTone Tone => Kind switch
    {
        BannerKind.ModelMissing or BannerKind.Downloading or BannerKind.SlowProcessing => BannerTone.Info,
        BannerKind.MicrophoneDenied or BannerKind.NoMicrophone or BannerKind.NoSound or BannerKind.NoSpeechFound => BannerTone.Warn,
        _ => BannerTone.Bad
    };

    public IReadOnlyList<BannerAction> Actions => Kind switch
    {
        BannerKind.ModelMissing or BannerKind.DownloadFailed => [BannerAction.Download],
        BannerKind.Downloading or BannerKind.FileFailed or BannerKind.NoSpeechFound => [BannerAction.Dismiss],
        BannerKind.TargetLost => [BannerAction.Retarget, BannerAction.Copy],
        BannerKind.MicrophoneDenied => [BannerAction.OpenMicrophoneSettings, BannerAction.Dismiss],
        BannerKind.NoMicrophone or BannerKind.NoSound => [BannerAction.OpenInputSettings, BannerAction.Dismiss],
        BannerKind.SlowProcessing => [BannerAction.UseOriginal, BannerAction.KeepWaiting],
        _ => []
    };
}

/// <summary>The key that writes the result; Shift+Enter always inserts a line break.</summary>
public enum CommitKey
{
    Enter,
    CtrlEnter
}

/// <summary>A file format of the export dialog.</summary>
public enum ExportFormat
{
    Txt,
    Markdown,
    Srt
}

/// <summary>Short messages shown below the composer; they replace dialogs for blocked actions.</summary>
public enum ToastKind
{
    Copied,
    Cancelled,
    Empty,
    Unavailable,
    Processing,
    Exported,
    ExportUnfinished
}

/// <summary>Everything the footer status line is built from.</summary>
public sealed record StatusInfo(
    ComposerStatus Status,
    bool Written,
    int RecordingSeconds,
    bool Armed,
    int Units,
    ComposerOutput Output,
    bool UsesCloud,
    int TranscribePercent);

/// <summary>Settings the composer reads once when it opens.</summary>
/// <param name="CloudTranslate">Translation runs on the user's cloud endpoint (shown in the privacy chip).</param>
/// <param name="CloudClean">Cleanup runs on the user's cloud endpoint (shown in the privacy chip).</param>
/// <param name="Animations">False when the user asked to reduce motion.</param>
/// <param name="Theme">"Auto" (follow the system), "Light" or "Dark".</param>
/// <param name="Cleanup">False when the user turned cleanup off: "Clean" is not offered and the composer starts on the original wording.</param>
public sealed record ComposerOptions(bool CloudTranslate, bool CloudClean, CommitKey CommitKey, bool Animations, string Theme, bool Cleanup)
{
    public static ComposerOptions Default { get; } = new(false, false, CommitKey.Enter, true, "Auto", true);

    /// <summary>The outputs the composer offers: "Clean" only while cleanup is on; the original wording is always there.</summary>
    public IReadOnlyList<ComposerOutput> Outputs => Cleanup
        ? [ComposerOutput.Clean, ComposerOutput.Translate, ComposerOutput.Original]
        : [ComposerOutput.Translate, ComposerOutput.Original];
}

/// <summary>Where a composer begins: the output and the translation direction, and what they came from.</summary>
/// <param name="DefaultOutput">What the settings say for an application without a remembered choice ("restore defaults" goes back to it).</param>
/// <param name="Remember">The context whose choices are remembered; null when remembering is off or the target cannot be identified.</param>
/// <param name="LastExport">The format exported last, which the export dialog offers first.</param>
public sealed record ComposerStart(
    ComposerOutput Output,
    Core.Translation.TargetLanguage Target,
    ComposerOutput DefaultOutput,
    Core.Translation.TargetLanguage DefaultTarget,
    RememberContext? Remember,
    ExportFormat? LastExport = null);

/// <param name="AppId">Identity of the application (or the clipboard context) the choices belong to.</param>
/// <param name="Name">What the user knows it as; empty for the clipboard context (the window says "copy only").</param>
/// <param name="Applied">The start is a remembered choice that differs from the defaults, which the composer says so.</param>
public sealed record RememberContext(string AppId, string Name, bool Applied);

/// <summary>What the user chose in the composer: the output and the direction in force after the choice.</summary>
public sealed record ComposerChoice(ComposerOutput Output, Core.Translation.TargetLanguage Target);
