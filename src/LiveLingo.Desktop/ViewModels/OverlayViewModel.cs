using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
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
    private readonly IModelManager? _modelManager;
    private readonly IMessenger _messenger;
    private readonly IReadOnlyList<LanguageInfo> _availableLanguages;
    private CancellationTokenSource? _pipelineCts;
    private string _postProcessMode;
    private string? _activeModelId;
    private bool _isApplyingRuntimeSettings;
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
    [ObservableProperty] private string _activeModelLabel = string.Empty;
    [ObservableProperty] private string _activeModelTooltip = string.Empty;
    [ObservableProperty] private bool _isSending;

    public string CopyLabel => L("overlay.copy");
    public string CopiedLabel => L("overlay.copied");
    public string SourceHint => L("overlay.sourceHint");

    public IReadOnlyList<LanguageInfo> AvailableTargetLanguages => _availableLanguages;

    public nint TargetWindowHandle => _targetWindow.Handle;
    public nint TargetInputChild => _targetWindow.InputChildHandle;
    public bool AutoSend => Mode == InjectionMode.PasteAndSend;

    public OverlayViewModel(
        TargetWindowInfo targetWindow,
        ITranslationPipeline pipeline,
        ITextInjector injector,
        ITranslationEngine engine,
        SettingsModel settings,
        IClipboardService? clipboard = null,
        ILocalizationService? localizationService = null,
        ISettingsService? settingsService = null,
        ILogger<OverlayViewModel>? logger = null,
        IModelManager? modelManager = null,
        IMessenger? messenger = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _engine = engine;
        _clipboard = clipboard;
        _loc = localizationService;
        _settingsService = settingsService;
        _logger = logger;
        _modelManager = modelManager;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = engine.SupportedLanguages;
        _activeModelId = settings.Translation.ActiveTranslationModelId;
        _sourceLanguage = string.IsNullOrWhiteSpace(settings.Translation.DefaultSourceLanguage)
            ? null
            : settings.Translation.DefaultSourceLanguage;
        var configuredTarget = settings.Translation.DefaultTargetLanguage;
        if (TryResolveTranslationPairFromModelId(_activeModelId, out var activeSource, out var activeTarget))
        {
            _sourceLanguage = activeSource;
            configuredTarget = activeTarget;
        }

        _targetLanguage = NormalizeTargetLanguage(configuredTarget);
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
        UpdateActiveModelDisplay();
        _messenger.Register<OverlayViewModel, SettingsChangedMessage>(this, static (recipient, _) =>
        {
            if (recipient._settingsService is not null)
                recipient.ApplySettings(recipient._settingsService.Current);
        });
    }

    public OverlayViewModel(
        TargetWindowInfo targetWindow,
        ITranslationPipeline pipeline,
        ITextInjector injector,
        ITranslationEngine engine,
        string targetLanguage = "en",
        IClipboardService? clipboard = null,
        ILocalizationService? localizationService = null,
        ILogger<OverlayViewModel>? logger = null,
        IMessenger? messenger = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _engine = engine;
        _clipboard = clipboard;
        _loc = localizationService;
        _logger = logger;
        _modelManager = null;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = engine.SupportedLanguages;
        _targetLanguage = NormalizeTargetLanguage(targetLanguage);
        _activeModelId = ModelRegistry.FindTranslationModel(_sourceLanguage ?? "zh", _targetLanguage)?.Id;
        _currentLangIndex = FindLanguageIndex(_targetLanguage);
        SelectedTargetLanguage = _availableLanguages.Count > 0 ? _availableLanguages[_currentLangIndex] : null;
        _postProcessMode = "Off";
        Mode = InjectionMode.PasteAndSend;
        _initialSourceLanguage = _sourceLanguage;
        _initialTargetLanguage = _targetLanguage;
        _initialMode = Mode;
        UpdateModeDisplay();
        UpdateActiveModelDisplay();
        _messenger.Register<OverlayViewModel, SettingsChangedMessage>(this, static (recipient, _) =>
        {
            if (recipient._settingsService is not null)
                recipient.ApplySettings(recipient._settingsService.Current);
        });
    }

    partial void OnSelectedTargetLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;
        TargetLanguage = value.Code;
        _currentLangIndex = FindLanguageIndex(value.Code);

        UpdateActiveModelDisplay();

        if (_isApplyingRuntimeSettings)
            return;

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

            var effectivePostProcessMode = ResolveEffectivePostProcessMode();
            var postProcessing = effectivePostProcessMode switch
            {
                "Summarize" => new ProcessingOptions(Summarize: true),
                "Optimize" => new ProcessingOptions(Optimize: true),
                "Colloquialize" => new ProcessingOptions(Colloquialize: true),
                _ => null
            };
            var postProcessingSkippedForMissingModel = false;

            if (postProcessing is not null && !IsQwenModelInstalled())
            {
                StatusText = L("overlay.error.modelNotDownloaded");
                _logger?.LogWarning(
                    "Post-processing model {ModelId} is not installed; running translation-only path",
                    ModelRegistry.Qwen25_15B.Id);
                postProcessing = null;
                postProcessingSkippedForMissingModel = true;
            }

            var result = await _pipeline.ProcessAsync(
                new TranslationRequest(text, _sourceLanguage, TargetLanguage, postProcessing), ct);
            TranslatedText = result.Text;

            var timing = $"{result.TranslationDuration.TotalMilliseconds:0}ms";
            StatusText = postProcessingSkippedForMissingModel
                ? L("overlay.error.modelNotDownloaded")
                : result.PostProcessingDuration is { } pp
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

    public async Task InjectAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(TranslatedText)) return;
        await _injector.InjectAsync(_targetWindow, TranslatedText, AutoSend, ct);
    }

    public async Task SendAsync(CancellationToken ct = default)
    {
        if (IsSending || string.IsNullOrWhiteSpace(TranslatedText))
            return;

        try
        {
            IsSending = true;
            await InjectAsync(ct);
            if (!ct.IsCancellationRequested)
                _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseOverlay)));
        }
        catch (OperationCanceledException)
        {
            // Expected on window/app shutdown.
        }
        finally
        {
            IsSending = false;
        }
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

        UpdateActiveModelDisplay();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.OpenSettings)));
    }

    public void ApplySettings(SettingsModel settings)
    {
        var previousSource = _sourceLanguage;
        var previousTarget = TargetLanguage;
        var previousPostProcessMode = _postProcessMode;
        var previousActiveModelId = _activeModelId;
        var previousMode = Mode;

        _isApplyingRuntimeSettings = true;
        try
        {
            _activeModelId = settings.Translation.ActiveTranslationModelId;
            var configuredSource = settings.Translation.DefaultSourceLanguage;
            var configuredTarget = settings.Translation.DefaultTargetLanguage;

            if (TryResolveTranslationPairFromModelId(_activeModelId, out var activeSource, out var activeTarget))
            {
                configuredSource = activeSource;
                configuredTarget = activeTarget;
            }

            _sourceLanguage = string.IsNullOrWhiteSpace(configuredSource) ? null : configuredSource;
            SelectedSourceLanguage = _availableLanguages.FirstOrDefault(l =>
                string.Equals(l.Code, _sourceLanguage, StringComparison.OrdinalIgnoreCase));

            var normalizedTarget = NormalizeTargetLanguage(configuredTarget);
            TargetLanguage = normalizedTarget;
            _currentLangIndex = FindLanguageIndex(normalizedTarget);
            SelectedTargetLanguage = _availableLanguages.Count > 0 ? _availableLanguages[_currentLangIndex] : null;

            _postProcessMode = settings.Processing.DefaultMode;
            Mode = settings.UI.DefaultInjectionMode == "PasteOnly"
                ? InjectionMode.PasteOnly
                : InjectionMode.PasteAndSend;
            UpdateModeDisplay();
            UpdateActiveModelDisplay();
        }
        finally
        {
            _isApplyingRuntimeSettings = false;
        }

        var translationConfigChanged = !string.Equals(previousSource, _sourceLanguage, StringComparison.OrdinalIgnoreCase) ||
                                       !string.Equals(previousTarget, TargetLanguage, StringComparison.OrdinalIgnoreCase);
        var postProcessChanged = !string.Equals(previousPostProcessMode, _postProcessMode, StringComparison.OrdinalIgnoreCase);
        var activeModelChanged = !string.Equals(previousActiveModelId, _activeModelId, StringComparison.OrdinalIgnoreCase);
        var modeChanged = previousMode != Mode;

        if ((translationConfigChanged || postProcessChanged || activeModelChanged || modeChanged) && !string.IsNullOrWhiteSpace(SourceText))
        {
            _pipelineCts?.Cancel();
            _pipelineCts = new CancellationTokenSource();
            _ = DebounceAndTranslateAsync(SourceText, _pipelineCts.Token);
        }
    }

    public void PersistIfChanged()
    {
        if (_settingsService is null) return;

        var sourceChanged = !string.Equals(_sourceLanguage, _initialSourceLanguage, StringComparison.OrdinalIgnoreCase);
        var targetChanged = !string.Equals(TargetLanguage, _initialTargetLanguage, StringComparison.OrdinalIgnoreCase);
        var modeChanged = Mode != _initialMode;

        if (!sourceChanged && !targetChanged && !modeChanged) return;

        var next = _settingsService.CloneCurrent();
        next.Translation.DefaultSourceLanguage = _sourceLanguage ?? next.Translation.DefaultSourceLanguage;
        next.Translation.DefaultTargetLanguage = TargetLanguage;
        next.Translation.ActiveTranslationModelId = ResolvePersistedActiveModelId(
            _sourceLanguage ?? next.Translation.DefaultSourceLanguage,
            TargetLanguage);
        next.UI.DefaultInjectionMode = Mode.ToString();
        _settingsService.Replace(next);
    }

    [RelayCommand]
    private void Cancel()
    {
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseOverlay)));
    }

    private bool IsQwenModelInstalled()
    {
        if (_modelManager is null)
            return true;

        return _modelManager.ListInstalled()
            .Any(m => string.Equals(m.Id, ModelRegistry.Qwen25_15B.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateActiveModelDisplay()
    {
        var source = _sourceLanguage ?? string.Empty;
        var translationModel = ResolveActiveTranslationModel(source, TargetLanguage);
        var translationLabel = translationModel?.DisplayName ?? $"Translation: {(string.IsNullOrWhiteSpace(source) ? "auto" : source)}→{TargetLanguage}";
        var translationId = translationModel?.Id ?? $"pair:{(string.IsNullOrWhiteSpace(source) ? "auto" : source)}-{TargetLanguage}";

        var selectedPostModel = ResolveSelectedPostProcessingModel();
        if (selectedPostModel is not null || !string.Equals(_postProcessMode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            var postLabelBase = selectedPostModel?.DisplayName ?? ModelRegistry.Qwen25_15B.DisplayName;
            var postModelId = selectedPostModel?.Id ?? ModelRegistry.Qwen25_15B.Id;
            var postLabel = IsQwenModelInstalled()
                ? postLabelBase
                : $"{postLabelBase} (not installed)";
            ActiveModelLabel = $"{translationLabel} + {postLabel}";
            ActiveModelTooltip = $"{translationId} + {postModelId}";
            return;
        }

        ActiveModelLabel = translationLabel;
        ActiveModelTooltip = translationId;
    }

    private string ResolveEffectivePostProcessMode()
    {
        var selectedPostModel = ResolveSelectedPostProcessingModel();
        if (selectedPostModel is not null && string.Equals(_postProcessMode, "Off", StringComparison.OrdinalIgnoreCase))
            return "Optimize";
        return _postProcessMode;
    }

    private ModelDescriptor? ResolveSelectedPostProcessingModel()
    {
        if (string.IsNullOrWhiteSpace(_activeModelId))
            return null;

        return ModelRegistry.AllModels.FirstOrDefault(m =>
            m.Type == ModelType.PostProcessing &&
            string.Equals(m.Id, _activeModelId, StringComparison.OrdinalIgnoreCase));
    }

    private ModelDescriptor? ResolveActiveTranslationModel(string source, string target)
    {
        if (TryResolveTranslationPairFromModelId(_activeModelId, out var pairSource, out var pairTarget))
            return ModelRegistry.FindTranslationModel(pairSource, pairTarget);

        return ModelRegistry.FindTranslationModel(source, target);
    }

    private string? ResolvePersistedActiveModelId(string sourceLanguage, string targetLanguage)
    {
        if (TryResolveTranslationPairFromModelId(_activeModelId, out _, out _))
            return ModelRegistry.FindTranslationModel(sourceLanguage, targetLanguage)?.Id ?? _activeModelId;

        if (!string.IsNullOrWhiteSpace(_activeModelId))
            return _activeModelId;

        return ModelRegistry.FindTranslationModel(sourceLanguage, targetLanguage)?.Id;
    }

    private static bool TryResolveTranslationPairFromModelId(string? modelId, out string source, out string target)
    {
        source = string.Empty;
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        if (!modelId.StartsWith("opus-mt-", StringComparison.OrdinalIgnoreCase))
            return false;

        var pair = modelId["opus-mt-".Length..];
        var parts = pair.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        source = parts[0];
        target = parts[1];
        return true;
    }
}
