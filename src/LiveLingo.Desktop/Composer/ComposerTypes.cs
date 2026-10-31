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

/// <summary>One line of the "will be written" pane.</summary>
/// <param name="Time">Where the line starts in an imported recording ("1:05"); empty for typed or spoken text.</param>
public sealed record ResultLine(int Index, string Text, LineState State, string Error, string Time = "");

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

    /// <summary>The microphone is open but only silence arrives.</summary>
    NoSound,

    /// <summary>A recording could not be read, or subtitles could not be saved (unsupported format, unreadable or unwritable file).</summary>
    FileFailed,

    /// <summary>The recording was read but contains no recognizable speech.</summary>
    NoSpeechFound
}

/// <param name="Detail">Size, error text or other detail for the view to show next to the localized message.</param>
/// <param name="Progress">0–100 while downloading.</param>
public sealed record BannerState(BannerKind Kind, string Detail = "", double Progress = 0)
{
    public static BannerState None { get; } = new(BannerKind.None);
}
