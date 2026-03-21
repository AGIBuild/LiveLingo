using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Speech;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Speech;
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
    private readonly IClipboardService? _clipboard;
    private readonly ILocalizationService? _loc;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<OverlayViewModel>? _logger;
    private readonly IMessenger _messenger;
    private readonly IReadOnlyList<LanguageInfo> _availableLanguages;
    private readonly ISpeechInputCoordinator? _speechCoordinator;
    private CancellationTokenSource? _pipelineCts;
    private string _postProcessMode;
    private string? _activeModelId;
    private bool _isApplyingRuntimeSettings;
    private bool _postProcessingDisabledForSession;
    private bool _postProcessingFallbackNoticeShown;
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
    [ObservableProperty] private VoiceInputState _voiceState = VoiceInputState.Idle;
    [ObservableProperty] private string _voiceStatusText = string.Empty;
    [ObservableProperty] private bool _isVoiceAvailable;
    [ObservableProperty] private bool _showSttDownloadLink;
    [ObservableProperty] private bool _isVoiceLanguagePickerOpen;
    private string _preRecordingText = string.Empty;
    [ObservableProperty] private LanguageInfo? _selectedVoiceLanguage;

    public string CopyLabel => L("overlay.copy");
    public string CopiedLabel => L("overlay.copied");
    public string SourceHint => L("overlay.sourceHint");
    public string AppTitle => L("app.name");
    public string SettingsTooltip => L("overlay.tooltip.settings");
    public string CloseTooltip => L("overlay.tooltip.close");
    public string SourceLanguageTooltip => L("overlay.tooltip.sourceLanguage");
    public string TargetLanguageTooltip => L("overlay.tooltip.targetLanguage");
    public string SwapLanguagesTooltip => L("overlay.tooltip.swapLanguage");
    public string SendLabel => L("overlay.send");
    public string SendTooltip => L("overlay.tooltip.send");
    public bool IsRecording => VoiceState == VoiceInputState.Recording;
    public string VoiceTooltip => VoiceState == VoiceInputState.Recording
        ? L("overlay.voice.tooltip.recording")
        : L("overlay.voice.tooltip");
    public string DownloadModelLabel => L("overlay.voice.downloadModel");
    public string SourceLanguageCodeDisplay => SelectedSourceLanguage?.Code ?? L("overlay.language.auto");
    public string VoiceLanguageDisplay => SelectedVoiceLanguage?.Code ?? L("overlay.language.auto");
    public IReadOnlyList<LanguageInfo> AvailableVoiceLanguages => _availableLanguages;

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
        IMessenger? messenger = null,
        ILanguageCatalog? languageCatalog = null,
        ISpeechInputCoordinator? speechCoordinator = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _clipboard = clipboard;
        _loc = localizationService;
        _settingsService = settingsService;
        _logger = logger;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
        _speechCoordinator = speechCoordinator;
        _isVoiceAvailable = speechCoordinator is not null;
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
        SubscribeSpeechCoordinator();
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
        IMessenger? messenger = null,
        ILanguageCatalog? languageCatalog = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _clipboard = clipboard;
        _loc = localizationService;
        _logger = logger;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
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

    partial void OnSelectedSourceLanguageChanged(LanguageInfo? value)
    {
        OnPropertyChanged(nameof(SourceLanguageCodeDisplay));
    }

    partial void OnSelectedVoiceLanguageChanged(LanguageInfo? value)
    {
        OnPropertyChanged(nameof(VoiceLanguageDisplay));
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

        var english = _availableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, "en", StringComparison.OrdinalIgnoreCase));
        return english?.Code ?? _availableLanguages[0].Code;
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
        // Increased debounce from 400ms to 800ms to reduce LLM churn
        await Task.Delay(800, ct);
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

            var postProcessing = _postProcessMode switch
            {
                "Summarize" => new ProcessingOptions(Summarize: true),
                "Optimize" => new ProcessingOptions(Optimize: true),
                "Colloquialize" => new ProcessingOptions(Colloquialize: true),
                _ => null
            };

            if (_postProcessingDisabledForSession)
                postProcessing = null;

            var degradedToTranslationOnly = false;
            var showFallbackNotice = false;
            TranslationResult result;
            try
            {
                result = await _pipeline.ProcessAsync(
                    new TranslationRequest(text, _sourceLanguage, TargetLanguage, postProcessing), ct);
            }
            catch (ModelNotReadyException ex) when (
                postProcessing is not null &&
                ex.ModelType == ModelType.PostProcessing)
            {
                _postProcessingDisabledForSession = true;
                showFallbackNotice = !_postProcessingFallbackNoticeShown;
                _postProcessingFallbackNoticeShown = true;

                _logger?.LogInformation(
                    "Post-processing model missing; fallback to translation-only. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}, ModelId={ModelId}",
                    _sourceLanguage ?? "<auto>",
                    TargetLanguage,
                    ex.ModelId);
                degradedToTranslationOnly = true;
                result = await _pipeline.ProcessAsync(
                    new TranslationRequest(text, _sourceLanguage, TargetLanguage, null), ct);
            }
            TranslatedText = result.Text;

            var timing = $"{result.TranslationDuration.TotalMilliseconds:0}ms";
            StatusText = degradedToTranslationOnly && showFallbackNotice
                ? L("overlay.postprocess.fallback")
                : result.PostProcessingDuration is { } pp
                ? L("overlay.translatedWithPost", timing, $"{pp.TotalMilliseconds:0}ms")
                : L("overlay.translated", timing);
        }
        catch (OperationCanceledException) { }
        catch (ModelNotReadyException ex)
        {
            StatusText = L("overlay.error.modelNotDownloaded");
            _logger?.LogInformation(
                "Translation failed because required model is not ready. ModelType={ModelType}, ModelId={ModelId}",
                ex.ModelType,
                ex.ModelId);
        }
        catch (FileNotFoundException)
        {
            StatusText = L("overlay.error.modelNotDownloaded");
            _logger?.LogError(
                "Translation failed: model file not found. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}",
                _sourceLanguage ?? "<auto>",
                TargetLanguage);
        }
        catch (NotSupportedException ex)
        {
            StatusText = L("overlay.error.modelNotDownloaded");
            _logger?.LogWarning(
                ex,
                "Translation pair is unavailable. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}",
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
            _postProcessingDisabledForSession = false;
            _postProcessingFallbackNoticeShown = false;
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
        _speechCoordinator?.CancelCurrent();
        _messenger.Send(new AppUiRequestMessage(new AppUiRequest(this, AppUiRequestKind.CloseOverlay)));
    }

    [RelayCommand]
    private async Task ToggleVoiceInputAsync()
    {
        if (_speechCoordinator is null) return;

        ShowSttDownloadLink = false;

        if (VoiceState == VoiceInputState.Recording)
        {
            VoiceStatusText = L("overlay.voice.transcribing");
            IsVoiceLanguagePickerOpen = false;
            var voiceLang = SelectedVoiceLanguage?.Code;
            var result = await _speechCoordinator.StopAndTranscribeAsync(voiceLang);
            if (result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    SourceText = _preRecordingText + result.Text;
                    if (SelectedVoiceLanguage is not null)
                    {
                        SelectedSourceLanguage = SelectedVoiceLanguage;
                        _sourceLanguage = SelectedVoiceLanguage.Code;
                    }
                }
                else
                {
                    VoiceStatusText = L("overlay.voice.noSpeech");
                }
            }
            else if (result.ErrorCode != SpeechInputErrorCode.Cancelled)
            {
                VoiceStatusText = MapVoiceError(result);
                ShowSttDownloadLink = result.ErrorCode == SpeechInputErrorCode.ModelMissing;
            }
        }
        else if (VoiceState == VoiceInputState.Idle || VoiceState == VoiceInputState.Error)
        {
            VoiceStatusText = string.Empty;
            _preRecordingText = SourceText;
            if (!string.IsNullOrEmpty(_preRecordingText) && !_preRecordingText.EndsWith(' '))
                _preRecordingText += " ";
            SelectedVoiceLanguage = SelectedSourceLanguage;
            var result = await _speechCoordinator.StartRecordingAsync(SelectedVoiceLanguage?.Code);
            if (!result.Success)
            {
                VoiceStatusText = MapVoiceError(result);
                ShowSttDownloadLink = result.ErrorCode == SpeechInputErrorCode.ModelMissing;
            }
            else
            {
                VoiceStatusText = L("overlay.voice.recording");
            }
        }
    }

    [RelayCommand]
    private void ToggleVoiceLanguagePicker()
    {
        IsVoiceLanguagePickerOpen = !IsVoiceLanguagePickerOpen;
    }

    [RelayCommand]
    private void SelectVoiceLanguage(LanguageInfo lang)
    {
        SelectedVoiceLanguage = lang;
        IsVoiceLanguagePickerOpen = false;
    }

    [RelayCommand]
    private async Task DownloadSttModelAsync()
    {
        if (_speechCoordinator is null) return;

        ShowSttDownloadLink = false;
        VoiceStatusText = L("overlay.voice.downloading");
        var result = await _speechCoordinator.EnsureSttModelAsync();
        if (result.Success)
        {
            VoiceStatusText = L("overlay.voice.modelReady");
        }
        else
        {
            VoiceStatusText = MapVoiceError(result);
            ShowSttDownloadLink = result.ErrorCode == SpeechInputErrorCode.ModelMissing;
        }
    }

    private void SubscribeSpeechCoordinator()
    {
        if (_speechCoordinator is null) return;
        _speechCoordinator.StateChanged += HandleVoiceStateChanged;
        _speechCoordinator.PartialTranscription += HandlePartialTranscription;
    }

    private void HandlePartialTranscription(string text)
    {
        if (VoiceState == VoiceInputState.Recording)
            SourceText = _preRecordingText + text;
    }

    private void HandleVoiceStateChanged(VoiceInputState state)
    {
        VoiceState = state;
        if (state == VoiceInputState.Idle)
        {
            VoiceStatusText = string.Empty;
            ShowSttDownloadLink = false;
        }
        OnPropertyChanged(nameof(VoiceTooltip));
        OnPropertyChanged(nameof(IsRecording));
    }

    private string MapVoiceError(SpeechInputResult result) => result.ErrorCode switch
    {
        SpeechInputErrorCode.PermissionDenied => L("overlay.voice.permissionDenied"),
        SpeechInputErrorCode.ModelMissing => L("overlay.voice.modelMissing"),
        SpeechInputErrorCode.PlatformNotSupported => L("overlay.voice.platformNotSupported"),
        SpeechInputErrorCode.AlreadyRecording => L("overlay.voice.alreadyRecording"),
        SpeechInputErrorCode.TranscriptionFailed => result.ErrorMessage ?? L("overlay.voice.transcriptionFailed"),
        _ => result.ErrorMessage ?? L("overlay.voice.error")
    };

    private void UpdateActiveModelDisplay()
    {
        var source = _sourceLanguage ?? string.Empty;
        var translationModel = ResolveActiveTranslationModel(source, TargetLanguage);
        var sourceLabel = string.IsNullOrWhiteSpace(source) ? L("overlay.language.auto") : source;
        var sourceIdLabel = string.IsNullOrWhiteSpace(source) ? "auto" : source;
        var translationLabel = translationModel?.DisplayName ??
                               $"{L("overlay.model.translationPrefix")}: {sourceLabel}→{TargetLanguage}";
        var translationId = translationModel?.Id ?? $"pair:{sourceIdLabel}-{TargetLanguage}";

        ActiveModelLabel = translationLabel;
        ActiveModelTooltip = translationId;
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
