using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.ViewModels;

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
    private readonly ITranslationEngine _engine;
    private readonly IClipboardService? _clipboard;
    private readonly ILocalizationService? _loc;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<OverlayViewModel>? _logger;
    private readonly IReadOnlyList<LanguageInfo> _availableLanguages;
    private CancellationTokenSource? _pipelineCts;
    private readonly string _postProcessMode;
    private string? _sourceLanguage;
    private int _currentLangIndex;
    private readonly string? _initialSourceLanguage;
    private readonly string _initialTargetLanguage;
    private readonly InjectionMode _initialMode;

    [ObservableProperty] private string _sourceText = string.Empty;
    [ObservableProperty] private string _translatedText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private InjectionMode _mode;
    [ObservableProperty] private string _modeLabel = string.Empty;
    [ObservableProperty] private string _targetLanguage;
    [ObservableProperty] private LanguageInfo? _selectedTargetLanguage;
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private bool _showCopiedFeedback;
    [ObservableProperty] private bool _isLanguagePickerOpen;
    [ObservableProperty] private int _sourceTextLength;
    [ObservableProperty] private LanguageInfo? _selectedSourceLanguage;

    public string CopyLabel => L("overlay.copy");
    public string CopiedLabel => L("overlay.copied");
    public string SourceHint => L("overlay.sourceHint");

    public IReadOnlyList<LanguageInfo> AvailableTargetLanguages => _availableLanguages;

    public nint TargetWindowHandle => _targetWindow.Handle;
    public nint TargetInputChild => _targetWindow.InputChildHandle;
    public bool AutoSend => Mode == InjectionMode.PasteAndSend;

    public event Action? RequestClose;
    public event Action? RequestOpenSettings;

    public OverlayViewModel(
        TargetWindowInfo targetWindow,
        ITranslationPipeline pipeline,
        ITextInjector injector,
        ITranslationEngine engine,
        UserSettings settings,
        IClipboardService? clipboard = null,
        ILocalizationService? localizationService = null,
        ISettingsService? settingsService = null,
        ILogger<OverlayViewModel>? logger = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _engine = engine;
        _clipboard = clipboard;
        _loc = localizationService;
        _settingsService = settingsService;
        _logger = logger;
        _availableLanguages = engine.SupportedLanguages;
        _sourceLanguage = string.IsNullOrWhiteSpace(settings.Translation.DefaultSourceLanguage)
            ? null
            : settings.Translation.DefaultSourceLanguage;
        _targetLanguage = NormalizeTargetLanguage(settings.Translation.DefaultTargetLanguage);
        _currentLangIndex = FindLanguageIndex(_targetLanguage);
        SelectedTargetLanguage = _availableLanguages.Count > 0 ? _availableLanguages[_currentLangIndex] : null;
        SelectedSourceLanguage = _availableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, _sourceLanguage, StringComparison.OrdinalIgnoreCase));
        _postProcessMode = settings.Processing.DefaultMode;

        Mode = settings.UI.DefaultInjectionMode == "PasteOnly"
            ? InjectionMode.PasteOnly
            : InjectionMode.PasteAndSend;

        _initialSourceLanguage = _sourceLanguage;
        _initialTargetLanguage = _targetLanguage;
        _initialMode = Mode;

        UpdateModeDisplay();
    }

    public OverlayViewModel(
        TargetWindowInfo targetWindow,
        ITranslationPipeline pipeline,
        ITextInjector injector,
        ITranslationEngine engine,
        string targetLanguage = "en",
        IClipboardService? clipboard = null,
        ILocalizationService? localizationService = null,
        ILogger<OverlayViewModel>? logger = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _engine = engine;
        _clipboard = clipboard;
        _loc = localizationService;
        _logger = logger;
        _availableLanguages = engine.SupportedLanguages;
        _targetLanguage = NormalizeTargetLanguage(targetLanguage);
        _currentLangIndex = FindLanguageIndex(_targetLanguage);
        SelectedTargetLanguage = _availableLanguages.Count > 0 ? _availableLanguages[_currentLangIndex] : null;
        _postProcessMode = "Off";
        Mode = InjectionMode.PasteAndSend;
        _initialSourceLanguage = _sourceLanguage;
        _initialTargetLanguage = _targetLanguage;
        _initialMode = Mode;
        UpdateModeDisplay();
    }

    partial void OnSelectedTargetLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;
        TargetLanguage = value.Code;
        _currentLangIndex = FindLanguageIndex(value.Code);

        if (!string.IsNullOrWhiteSpace(SourceText))
        {
            _pipelineCts?.Cancel();
            _pipelineCts = new CancellationTokenSource();
            _ = DebounceAndTranslateAsync(SourceText, _pipelineCts.Token);
        }
    }

    private int FindLanguageIndex(string code)
    {
        for (var i = 0; i < _availableLanguages.Count; i++)
            if (string.Equals(_availableLanguages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private string NormalizeTargetLanguage(string? code)
    {
        if (_availableLanguages.Count == 0)
            return string.IsNullOrWhiteSpace(code) ? "en" : code;

        if (!string.IsNullOrWhiteSpace(code))
        {
            var idx = FindLanguageIndex(code);
            if (idx >= 0 && idx < _availableLanguages.Count &&
                string.Equals(_availableLanguages[idx].Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return _availableLanguages[idx].Code;
            }
        }

        return _availableLanguages[0].Code;
    }

    partial void OnSourceTextChanged(string value)
    {
        SourceTextLength = value.Length;
        _pipelineCts?.Cancel();

        if (string.IsNullOrWhiteSpace(value))
        {
            TranslatedText = string.Empty;
            IsTranslating = false;
            return;
        }

        _pipelineCts = new CancellationTokenSource();
        _ = DebounceAndTranslateAsync(value, _pipelineCts.Token);
    }

    private async Task DebounceAndTranslateAsync(string text, CancellationToken ct)
    {
        await Task.Delay(400, ct);
        await RunPipelineAsync(text, ct);
    }

    private string L(string key) => _loc?.T(key) ?? key;
    private string L(string key, params object[] args) => _loc?.T(key, args) ?? key;

    private async Task RunPipelineAsync(string text, CancellationToken ct)
    {
        try
        {
            IsTranslating = true;
            StatusText = L("overlay.translating");

            if (string.IsNullOrWhiteSpace(TargetLanguage))
            {
                _logger?.LogWarning("Target language not configured");
                StatusText = L("overlay.error.targetNotConfigured");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_sourceLanguage) &&
                !_engine.SupportsLanguagePair(_sourceLanguage, TargetLanguage))
            {
                _logger?.LogWarning("Unsupported language pair: {Source} → {Target}", _sourceLanguage, TargetLanguage);
                StatusText = L("overlay.error.unsupportedPair", _sourceLanguage, TargetLanguage);
                return;
            }

            var postProcessing = _postProcessMode switch
            {
                "Summarize" => new ProcessingOptions(Summarize: true),
                "Optimize" => new ProcessingOptions(Optimize: true),
                "Colloquialize" => new ProcessingOptions(Colloquialize: true),
                _ => null
            };

            var result = await _pipeline.ProcessAsync(
                new TranslationRequest(text, _sourceLanguage, TargetLanguage, postProcessing), ct);
            TranslatedText = result.Text;

            var timing = $"{result.TranslationDuration.TotalMilliseconds:0}ms";
            StatusText = result.PostProcessingDuration is { } pp
                ? L("overlay.translatedWithPost", timing, $"{pp.TotalMilliseconds:0}ms")
                : L("overlay.translated", timing);
        }
        catch (OperationCanceledException) { }
        catch (FileNotFoundException)
        {
            StatusText = L("overlay.error.modelNotDownloaded");
            _logger?.LogError(
                "Translation failed: model file not found. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}",
                _sourceLanguage ?? "<auto>",
                TargetLanguage);
        }
        catch (Exception ex)
        {
            StatusText = L("overlay.error", ex.Message);
            _logger?.LogError(
                ex,
                "Translation pipeline failed. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}, TextLength={TextLength}",
                _sourceLanguage ?? "<auto>",
                TargetLanguage,
                text?.Length ?? 0);
        }
        finally
        {
            IsTranslating = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        Mode = Mode == InjectionMode.PasteAndSend
            ? InjectionMode.PasteOnly
            : InjectionMode.PasteAndSend;
        UpdateModeDisplay();
    }

    [RelayCommand]
    private void ToggleLanguagePicker()
    {
        IsLanguagePickerOpen = !IsLanguagePickerOpen;
    }

    [RelayCommand]
    private void SelectLanguage(LanguageInfo lang)
    {
        SelectedTargetLanguage = lang;
        IsLanguagePickerOpen = false;
    }

    [RelayCommand]
    private void CycleLanguage()
    {
        if (_availableLanguages.Count == 0) return;
        _currentLangIndex = (_currentLangIndex + 1) % _availableLanguages.Count;
        SelectedTargetLanguage = _availableLanguages[_currentLangIndex];
    }

    private void UpdateModeDisplay()
    {
        ModeLabel = Mode == InjectionMode.PasteAndSend
            ? L("overlay.pasteAndSend")
            : L("overlay.pasteOnly");
    }

    public async Task InjectAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslatedText)) return;
        await _injector.InjectAsync(_targetWindow, TranslatedText, AutoSend);
    }

    [RelayCommand]
    private async Task CopyTranslationAsync()
    {
        if (_clipboard is null || string.IsNullOrWhiteSpace(TranslatedText)) return;
        await _clipboard.SetTextAsync(TranslatedText);
        ShowCopiedFeedback = true;
        await Task.Delay(800);
        ShowCopiedFeedback = false;
    }

    [RelayCommand]
    private void SwapLanguages()
    {
        if (SelectedTargetLanguage is null) return;

        var oldSource = _sourceLanguage;
        var oldTarget = SelectedTargetLanguage;

        _sourceLanguage = oldTarget.Code;
        SelectedSourceLanguage = oldTarget;

        if (oldSource is not null)
        {
            var newTargetInfo = _availableLanguages.FirstOrDefault(l =>
                string.Equals(l.Code, oldSource, StringComparison.OrdinalIgnoreCase));
            if (newTargetInfo is not null)
            {
                SelectedTargetLanguage = newTargetInfo;
            }
        }

        if (!string.IsNullOrWhiteSpace(SourceText))
        {
            _pipelineCts?.Cancel();
            _pipelineCts = new CancellationTokenSource();
            _ = DebounceAndTranslateAsync(SourceText, _pipelineCts.Token);
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        RequestOpenSettings?.Invoke();
    }

    public void PersistIfChanged()
    {
        if (_settingsService is null) return;

        var sourceChanged = !string.Equals(_sourceLanguage, _initialSourceLanguage, StringComparison.OrdinalIgnoreCase);
        var targetChanged = !string.Equals(TargetLanguage, _initialTargetLanguage, StringComparison.OrdinalIgnoreCase);
        var modeChanged = Mode != _initialMode;

        if (!sourceChanged && !targetChanged && !modeChanged) return;

        _settingsService.Update(s => s with
        {
            Translation = s.Translation with
            {
                DefaultSourceLanguage = _sourceLanguage ?? s.Translation.DefaultSourceLanguage,
                DefaultTargetLanguage = TargetLanguage
            },
            UI = s.UI with
            {
                DefaultInjectionMode = Mode.ToString()
            }
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }
}
