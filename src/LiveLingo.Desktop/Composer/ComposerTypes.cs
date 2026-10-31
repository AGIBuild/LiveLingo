namespace LiveLingo.Desktop.Composer;

/// <summary>The output pill: what is written into the target.</summary>
public enum ComposerOutput
{
    Translate,
    Original
}

public enum ComposerStatus
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

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
public sealed record ResultLine(int Index, string Text, LineState State, string Error);

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
    WriteFailed
}

/// <param name="Detail">Size, error text or other detail for the view to show next to the localized message.</param>
/// <param name="Progress">0–100 while downloading.</param>
public sealed record BannerState(BannerKind Kind, string Detail = "", double Progress = 0)
{
    public static BannerState None { get; } = new(BannerKind.None);
}
