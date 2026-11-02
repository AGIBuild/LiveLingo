using LiveLingo.Desktop.Composer;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

/// <summary>The two native dialogs the composer needs; the window that hosts the page implements them.</summary>
internal interface IFileDialogs
{
    /// <summary>The recording to import, or null when the user cancelled.</summary>
    Task<string?> PickRecordingAsync();

    /// <summary>Where to save the export, or null when the user cancelled.</summary>
    Task<string?> PickExportPathAsync(ExportFormat format);
}

/// <summary>
/// Maps the page's intents onto the composer view model. Holds no decisions of its own: every rule (what may be
/// written, when a draft can be replaced, what a key means) stays in the view model.
/// </summary>
internal sealed class ComposerIntents : IIntentHandler
{
    private readonly ComposerViewModel _vm;
    private readonly RecentDrafts _recent;
    private readonly IFileDialogs _files;
    private readonly ILogger _logger;
    private string _pageText = "";

    public ComposerIntents(ComposerViewModel vm, RecentDrafts recent, IFileDialogs files, ILogger logger)
    {
        _vm = vm;
        _recent = recent;
        _files = files;
        _logger = logger;
        vm.PropertyChanged += (_, e) =>
        {
            // The text changed behind the page's back (speech, an import, a restored draft): newer than anything it sent.
            if (e.PropertyName == nameof(ComposerViewModel.SourceText) && vm.SourceText != _pageText)
            {
                _pageText = vm.SourceText;
                SourceRev++;
            }
        };
    }

    /// <summary>
    /// The revision of the source text. The page adopts the text of a snapshot only when this is greater than the last
    /// revision it sent, so typing (and an IME composition) is never overwritten by an echo (design 5.3). Starts at 1:
    /// a fresh page has sent nothing (0), and must show a restored draft.
    /// </summary>
    public int SourceRev { get; private set; } = 1;

    /// <summary>The page measured its content; the window follows.</summary>
    public event Action<SizeIntent>? SizeChanged;

    /// <summary>The user started dragging the composer by its header; the window is moved by the system.</summary>
    public event Action? Dragged;

    /// <summary>The user is dragging an edge or the corner: the window size the page asks for.</summary>
    public event Action<ResizeIntent>? Resized;

    /// <summary>The user asked for the large window, or for the size that follows the content again.</summary>
    public event Action? Zoomed;
    public event Action? ModelInfoRequested;
    public event Action? DraftCleared;

    /// <summary>State that is not on the view model changed (the recent drafts).</summary>
    public event Action? Changed;

    public void Handle(WebIntent intent)
    {
        switch (intent)
        {
            case SizeIntent size: SizeChanged?.Invoke(size); break;
            case DragIntent: Dragged?.Invoke(); break;
            case ResizeIntent resize: Resized?.Invoke(resize); break;
            case ZoomIntent: Zoomed?.Invoke(); break;
            case SourceIntent source:
                _pageText = source.Text;
                SourceRev = source.Rev;
                _vm.SourceText = source.Text;
                break;
            case MicToggleIntent: Run(() => _vm.ToggleListeningCommand.ExecuteAsync(null)); break;
            case OutputIntent output: _vm.ChooseOutput(output.Value); break;
            case RestoreDefaultsIntent: _vm.RestoreDefaultsCommand.Execute(null); break;
            case SwapLanguageIntent: _vm.SwapLanguageCommand.Execute(null); break;
            case CommitIntent commit: Run(() => _vm.CommitAsync(commit.FromKey)); break;
            case CopyIntent: Run(() => _vm.CopyCommand.ExecuteAsync(null)); break;
            case CancelIntent: Run(() => _vm.CancelCommand.ExecuteAsync(null)); break;
            case HideIntent: _vm.HideCommand.Execute(null); break;
            case RetryIntent retry: _vm.RetryCommand.Execute(retry.I); break;
            case EditIntent edit: _vm.StartEditCommand.Execute(edit.I); break;
            case EditTextIntent edit: _vm.EditText = edit.Text; break;
            case SaveEditIntent: _vm.SaveEditCommand.Execute(null); break;
            case CancelEditIntent: _vm.CancelEditCommand.Execute(null); break;
            case UseOriginalIntent original: _vm.UseOriginalCommand.Execute(original.I); break;
            case NewDraftIntent:
                Run(async () =>
                {
                    await _vm.NewDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
                    DraftCleared?.Invoke();
                });
                break;
            case ModelInfoIntent: ModelInfoRequested?.Invoke(); break;
            case RestoreRecentIntent restore: Restore(restore.Index); break;
            case ClearRecentIntent:
                _recent.Clear();
                Changed?.Invoke();
                break;
            case ImportIntent: Run(ImportAsync); break;
            case ExportIntent export: Run(() => ExportAsync(export.Format)); break;
            case WideIntent wide: _vm.ForceWide = wide.On; break;
            case WholeIntent whole: _vm.ForceWhole = whole.On; break;
            case CopyOnlyIntent copyOnly: _vm.CopyOnly = copyOnly.On; break;
            case BannerIntent banner: Run(() => _vm.RunBannerActionCommand.ExecuteAsync(banner.Action)); break;
            case OpenSettingsIntent settings: _vm.OpenSettings(settings.Section); break;
            default: throw new ArgumentOutOfRangeException(nameof(intent), intent.GetType().Name, "Not a composer intent.");
        }
    }

    private void Restore(int index)
    {
        if (_vm.CanReplaceDraft && index < _recent.Items.Count)
            _vm.SourceText = _recent.Items[index].Text;
    }

    private async Task ImportAsync()
    {
        if (await _files.PickRecordingAsync() is { } path)
            await _vm.ImportAsync(path);
    }

    private async Task ExportAsync(ExportFormat format)
    {
        if (await _files.PickExportPathAsync(format) is { } path)
            await _vm.ExportAsync(path, format);
    }

    private void Run(Func<Task> action) => Observed.Run(action, _logger, "A composer action");
}
