using CommunityToolkit.Mvvm.Input;

namespace LiveLingo.Desktop.Composer;

/// <summary>
/// The output and the translation direction as the user's own choices: where the composer begins, which changes count as
/// a choice (to be remembered for the application) and the way back to the defaults. What is remembered, and where, is
/// decided elsewhere (<c>ComposerPreferences</c>); the view model only tells.
/// </summary>
public sealed partial class ComposerViewModel
{
    private ComposerStart? _start;
    private bool _announceRemembered;

    /// <summary>The user chose an output or a direction. A switch the system makes (the slow-output banner) is not one.</summary>
    public event Action<ComposerChoice>? ChoiceMade;

    /// <summary>A file was exported in this format.</summary>
    public event Action<ExportFormat>? Exported;

    /// <summary>The format exported last (now or in an earlier composer): the export dialog offers it first.</summary>
    public ExportFormat? LastExportFormat { get; private set; }

    /// <summary>The user went back to the settings' defaults, so what was remembered for the application should be forgotten.</summary>
    public event Action? DefaultsRestored;

    /// <summary>Begins at a resolved start; not a choice of the user.</summary>
    public void Begin(ComposerStart start)
    {
        _start = start;
        _announceRemembered = start.Remember?.Applied == true;
        LastExportFormat = start.LastExport;
        Output = start.Output;
        TargetLanguage = start.Target;
        OnPropertyChanged(nameof(Remembered));
        OnPropertyChanged(nameof(RememberFor));
    }

    /// <summary>The application (or "copy only") whose choices are remembered; null when remembering is off or it is unknown.</summary>
    public RememberContext? RememberFor => _start?.Remember;

    /// <summary>
    /// Set while the composer stands on a remembered choice that differs from the defaults and the user has not
    /// changed anything since: the window says where the choice came from and offers the way back.
    /// </summary>
    public RememberContext? Remembered => _announceRemembered ? _start?.Remember : null;

    public void ChooseOutput(ComposerOutput output)
    {
        if (!Outputs.Contains(output))
            throw new ArgumentOutOfRangeException(nameof(output), output, "That output is not offered.");

        Output = output;
        Chosen();
    }

    private void Chosen()
    {
        _announceRemembered = false;
        OnPropertyChanged(nameof(Remembered));
        ChoiceMade?.Invoke(new ComposerChoice(Output, TargetLanguage));
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        if (_start is not { } start)
            return;

        _announceRemembered = false;
        Output = start.DefaultOutput;
        TargetLanguage = start.DefaultTarget;
        OnPropertyChanged(nameof(Remembered));
        DefaultsRestored?.Invoke();
    }
}
