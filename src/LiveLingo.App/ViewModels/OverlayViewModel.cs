using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Platform;
using LiveLingo.Core.Translation;

namespace LiveLingo.App.ViewModels;

public enum InjectionMode
{
    PasteOnly,
    PasteAndSend
}

public partial class OverlayViewModel : ObservableObject
{
    private readonly TargetWindowInfo _targetWindow;
    private readonly ITranslationPipeline _pipeline;
    private readonly ITextInjector _injector;
    private CancellationTokenSource? _pipelineCts;

    private static InjectionMode _lastMode = InjectionMode.PasteAndSend;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _translatedText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private InjectionMode _mode;

    [ObservableProperty]
    private string _modeLabel = string.Empty;

    public string TargetLanguage { get; }

    public nint TargetWindowHandle => _targetWindow.Handle;
    public nint TargetInputChild => _targetWindow.InputChildHandle;
    public bool AutoSend => Mode == InjectionMode.PasteAndSend;

    public event Action? RequestClose;

    public OverlayViewModel(
        TargetWindowInfo targetWindow,
        ITranslationPipeline pipeline,
        ITextInjector injector,
        string targetLanguage = "en")
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        TargetLanguage = targetLanguage;
        Mode = _lastMode;
        UpdateModeDisplay();
    }

    partial void OnSourceTextChanged(string value)
    {
        _pipelineCts?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            TranslatedText = string.Empty;
            return;
        }

        _pipelineCts = new CancellationTokenSource();
        _ = RunPipelineAsync(value, _pipelineCts.Token);
    }

    private async Task RunPipelineAsync(string text, CancellationToken ct)
    {
        try
        {
            StatusText = "Translating...";
            var result = await _pipeline.ProcessAsync(
                new TranslationRequest(text, null, TargetLanguage, null), ct);
            TranslatedText = result.Text;
            StatusText = $"Translated ({result.TranslationDuration.TotalMilliseconds:0}ms) · " +
                         (AutoSend ? "Ctrl+Enter paste & send" : "Ctrl+Enter paste") +
                         " · Esc cancel";
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = Mode == InjectionMode.PasteAndSend
            ? InjectionMode.PasteOnly
            : InjectionMode.PasteAndSend;

        _lastMode = Mode;
        UpdateModeDisplay();
    }

    private void UpdateModeDisplay()
    {
        if (Mode == InjectionMode.PasteAndSend)
        {
            ModeLabel = "Paste & Send";
            StatusText = "Ctrl+Enter paste & send · Esc cancel";
        }
        else
        {
            ModeLabel = "Paste Only";
            StatusText = "Ctrl+Enter paste · Esc cancel";
        }
    }

    public async Task InjectAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslatedText)) return;
        await _injector.InjectAsync(_targetWindow, TranslatedText, AutoSend);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }
}
