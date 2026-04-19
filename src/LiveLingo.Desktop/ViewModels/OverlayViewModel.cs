using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Speech;
using LiveLingo.Core;
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

public partial class OverlayViewModel : ObservableObject, IDisposable
{
    // Guards _pipelineCts so Cancel() can never race with the owning task's
    // Dispose() in its finally-block. Without this, a completed task would
    // dispose the CTS just as a new keystroke reads the field and called
    // Cancel() on the disposed instance, throwing ObjectDisposedException.
    private readonly object _pipelineCtsLock = new();
    private bool _disposed;

    private readonly TargetWindowInfo _targetWindow;
    private readonly ITranslationPipeline _pipeline;
    private readonly ITextInjector _injector;
    private readonly IClipboardService? _clipboard;
    private readonly ILocalizationService? _loc;
    private readonly ISettingsService? _settingsService;
    private readonly IModelManager? _modelManager;
    private readonly IModelCatalog _modelCatalog;
    private readonly ICloudProviderRuntimeState _cloudProviderRuntimeState;
    private readonly ILogger<OverlayViewModel>? _logger;
    private readonly IMessenger _messenger;
    private readonly IReadOnlyList<LanguageInfo> _availableLanguages;
    private readonly ISpeechInputCoordinator? _speechCoordinator;
    private readonly IModelDownloadCoordinator _downloadCoordinator;
    private readonly Action<ModelDownloadState>? _onDownloadStateChanged;
    private readonly SynchronizationContext? _uiContext;
    // STT model ids the overlay listens for; computed once because the registry is static.
    private static readonly HashSet<string> SttModelIds =
        ModelRegistry.SpeechToTextModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
    private CancellationTokenSource? _pipelineCts;
    private string _postProcessMode;
    private string? _activeModelId;
    private TranslationRoutingMode _routingMode = TranslationRoutingMode.PreferLocal;
    private bool _routeUnsupportedPairsToCloud = true;
    private CloudModelPreferences _cloudPreferences = new(false, null, null, null, null);
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
        ISpeechInputCoordinator? speechCoordinator = null,
        IModelCatalog? modelCatalog = null,
        ICloudProviderRuntimeState? cloudProviderRuntimeState = null,
        IModelDownloadCoordinator? downloadCoordinator = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _clipboard = clipboard;
        _loc = localizationService;
        _settingsService = settingsService;
        _modelManager = modelManager;
        _modelCatalog = modelCatalog ?? new StaticModelCatalog();
        _cloudProviderRuntimeState = cloudProviderRuntimeState ?? new NullCloudProviderRuntimeState();
        _logger = logger;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
        _speechCoordinator = speechCoordinator;
        _isVoiceAvailable = speechCoordinator is not null;
        // Listen on the global coordinator so STT downloads kicked off from
        // Settings → Models or the wizard project their progress here too.
        _downloadCoordinator = downloadCoordinator ?? NullModelDownloadCoordinator.Instance;
        _uiContext = SynchronizationContext.Current;
        _onDownloadStateChanged = OnDownloadStateChanged;
        _downloadCoordinator.StateChanged += _onDownloadStateChanged;
        ApplyRoutingSettings(settings);
        _activeModelId = GetPreferredLocalModelId(settings);
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
        ILanguageCatalog? languageCatalog = null,
        IModelCatalog? modelCatalog = null,
        ICloudProviderRuntimeState? cloudProviderRuntimeState = null)
    {
        _targetWindow = targetWindow;
        _pipeline = pipeline;
        _injector = injector;
        _clipboard = clipboard;
        _loc = localizationService;
        _logger = logger;
        _modelManager = null;
        _modelCatalog = modelCatalog ?? new StaticModelCatalog();
        _cloudProviderRuntimeState = cloudProviderRuntimeState ?? new NullCloudProviderRuntimeState();
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _availableLanguages = languageCatalog?.All ?? LanguageCatalog.DefaultLanguages;
        _downloadCoordinator = NullModelDownloadCoordinator.Instance;
        _targetLanguage = NormalizeTargetLanguage(targetLanguage);
        _activeModelId = ModelSelectionPolicy
            .SelectTranslationProfile(_modelCatalog, null, _sourceLanguage ?? "zh", _targetLanguage)
            .Id;
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
            ScheduleDebouncedTranslation(SourceText);
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

        if (string.IsNullOrWhiteSpace(value))
        {
            CancelActivePipeline();
            TranslatedText = string.Empty;
            IsTranslating = false;
            return;
        }

        // Long texts (> 600 chars) are automatically routed to the cloud quality tier
        // by TranslationRoutingContext inside the MEA IChatClient pipeline.
        // No hard truncation here — routing handles quality/capacity decisions.
        ScheduleDebouncedTranslation(value);
    }

    private void CancelActivePipeline()
    {
        lock (_pipelineCtsLock)
        {
            _pipelineCts?.Cancel();
        }
    }

    private void ScheduleDebouncedTranslation(string text)
    {
        CancellationTokenSource cts;
        lock (_pipelineCtsLock)
        {
            // Cancel the previous attempt; its owning task will dispose it
            // under the same lock, so the Cancel() call can never race with
            // Dispose() and throw ObjectDisposedException.
            _pipelineCts?.Cancel();

            if (_disposed) return;

            cts = new CancellationTokenSource();
            _pipelineCts = cts;
        }

        _ = DebounceAndTranslateAsync(cts, text);
    }

    private async Task DebounceAndTranslateAsync(CancellationTokenSource cts, string text)
    {
        try
        {
            // Increased debounce from 400ms to 800ms to reduce LLM churn
            await Task.Delay(800, cts.Token).ConfigureAwait(true);
            await RunPipelineAsync(text, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Debounce was interrupted by a newer keystroke; swallow silently.
        }
        finally
        {
            lock (_pipelineCtsLock)
            {
                // Only clear the field if we're still the active attempt; a
                // newer ScheduleDebouncedTranslation may have replaced it.
                if (ReferenceEquals(_pipelineCts, cts))
                    _pipelineCts = null;
                cts.Dispose();
            }
        }
    }

    private string L(string key) => _loc?.T(key) ?? key;
    private string L(string key, params object[] args) => _loc?.T(key, args) ?? key;

    /// <summary>
    /// Builds a progress callback that surfaces the pipeline's hidden waits
    /// (language detection, model warm-up) as status-bar updates. Callbacks
    /// fire on arbitrary threads so every mutation stays on the property's
    /// captured sync context by going through <see cref="Progress{T}"/>, which
    /// captures the current <see cref="SynchronizationContext"/>.
    /// </summary>
    private IProgress<TranslationLifecycleEvent> BuildLifecycleProgress()
    {
        return new Progress<TranslationLifecycleEvent>(evt =>
        {
            switch (evt.Phase)
            {
                case TranslationPhase.LanguageDetectionStarted:
                    StatusText = L("overlay.detectingLanguage");
                    break;
                case TranslationPhase.LanguageDetected:
                    if (!string.IsNullOrEmpty(evt.DetectedLanguage))
                    {
                        StatusText = L(
                            "overlay.translating.fromDetected",
                            FriendlyLanguageName(evt.DetectedLanguage),
                            FriendlyLanguageName(TargetLanguage));
                    }
                    break;
                case TranslationPhase.TranslationStarted:
                    // Only escalate back to the generic "translating…" label if
                    // detection did not already produce a more specific one.
                    if (string.IsNullOrEmpty(StatusText) ||
                        StatusText == L("overlay.detectingLanguage"))
                    {
                        StatusText = L("overlay.translating");
                    }
                    break;
                case TranslationPhase.FirstTokenReceived:
                    // Streaming output is now filling TranslatedText; no status
                    // change needed but logging helps profile cold-start latency.
                    _logger?.LogDebug(
                        "First translation token received after {Elapsed}ms",
                        evt.Elapsed.TotalMilliseconds);
                    break;
            }
        });
    }

    private string FriendlyLanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return L("overlay.language.auto");
        var match = _availableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        return match?.DisplayName ?? code;
    }

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

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Use streaming for translation-only requests; fall back to ProcessAsync when
            // post-processing is also needed (streaming and post-processing don't compose).
            if (postProcessing is null)
            {
                await RunStreamingPipelineAsync(text, sw, ct);
                return;
            }

            var progress = BuildLifecycleProgress();
            var degradedToTranslationOnly = false;
            var showFallbackNotice = false;
            TranslationResult result;
            try
            {
                result = await _pipeline.ProcessAsync(
                    new TranslationRequest(text, _sourceLanguage, TargetLanguage, postProcessing), ct, progress);
            }
            catch (ModelNotReadyException ex) when (
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
                    new TranslationRequest(text, _sourceLanguage, TargetLanguage, null), ct, progress);
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
        catch (TranslationFailedException ex)
        {
            StatusText = L("overlay.error.translationFailed");
            _logger?.LogWarning(
                ex,
                "Translation failed after model/runtime startup. SourceLanguage={SourceLanguage}, TargetLanguage={TargetLanguage}, TextLength={TextLength}",
                _sourceLanguage ?? "<auto>",
                TargetLanguage,
                text?.Length ?? 0);
        }
        catch (NotSupportedException ex)
        {
            StatusText = L("overlay.error.unsupportedPair", _sourceLanguage ?? L("overlay.language.auto"), TargetLanguage);
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

    private async Task RunStreamingPipelineAsync(
        string text, System.Diagnostics.Stopwatch sw, CancellationToken ct)
    {
        var translatedBuilder = new System.Text.StringBuilder();
        var progress = BuildLifecycleProgress();

        try
        {
            await foreach (var delta in _pipeline.ProcessStreamingAsync(
                               new TranslationRequest(text, _sourceLanguage, TargetLanguage, null), ct, progress)
                           .ConfigureAwait(false))
            {
                if (delta.IsReplacement)
                {
                    translatedBuilder.Clear();
                    translatedBuilder.Append(delta.Text);
                }
                else
                {
                    translatedBuilder.Append(delta.Text);
                }

                // Push partial text to the UI on every delta.
                TranslatedText = translatedBuilder.ToString().Trim();
            }

            StatusText = L("overlay.translated", $"{sw.Elapsed.TotalMilliseconds:0}ms");
        }
        catch (ModelNotReadyException ex)
        {
            StatusText = L("overlay.error.modelNotDownloaded");
            _logger?.LogInformation(
                "Streaming translation failed: model not ready. ModelType={ModelType}, ModelId={ModelId}",
                ex.ModelType, ex.ModelId);
        }
        catch (OperationCanceledException) { }
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
            ScheduleDebouncedTranslation(SourceText);

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
        var previousRoutingMode = _routingMode;
        var previousRouteUnsupportedPairsToCloud = _routeUnsupportedPairsToCloud;
        var previousCloudSignature = BuildCloudPreferencesSignature(_cloudPreferences);
        var previousMode = Mode;

        _isApplyingRuntimeSettings = true;
        try
        {
            ApplyRoutingSettings(settings);
            _activeModelId = GetPreferredLocalModelId(settings);
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
        var routingChanged = previousRoutingMode != _routingMode ||
                             previousRouteUnsupportedPairsToCloud != _routeUnsupportedPairsToCloud ||
                             !string.Equals(previousCloudSignature, BuildCloudPreferencesSignature(_cloudPreferences), StringComparison.Ordinal);
        var modeChanged = previousMode != Mode;

        if ((translationConfigChanged || postProcessChanged || activeModelChanged || routingChanged || modeChanged) &&
            !string.IsNullOrWhiteSpace(SourceText))
        {
            ScheduleDebouncedTranslation(SourceText);
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
        next.Translation.ModelPolicy.PreferredLocalTranslationModelId = next.Translation.ActiveTranslationModelId;
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

    private void OnDownloadStateChanged(ModelDownloadState state)
    {
        // Filter to STT models so unrelated translation/postprocessing downloads
        // don't bleed into the voice status banner.
        if (!SttModelIds.Contains(state.ModelId)) return;

        if (_uiContext is null)
        {
            ApplySttDownloadState(state);
            return;
        }

        _uiContext.Post(_ => ApplySttDownloadState(state), null);
    }

    private void ApplySttDownloadState(ModelDownloadState state)
    {
        if (_disposed) return;
        // Don't overwrite live recording / transcribing status text — those
        // states have higher priority than background download progress.
        if (VoiceState is VoiceInputState.Recording or VoiceInputState.Transcribing) return;

        switch (state.Status)
        {
            case ModelDownloadStatus.Downloading:
                VoiceStatusText = L("overlay.voice.downloadingProgress", state.Percentage);
                ShowSttDownloadLink = false;
                break;
            case ModelDownloadStatus.Installed:
                VoiceStatusText = L("overlay.voice.modelReady");
                ShowSttDownloadLink = false;
                break;
            case ModelDownloadStatus.Failed:
                VoiceStatusText = state.ErrorMessage ?? L("overlay.voice.transcriptionFailed");
                ShowSttDownloadLink = true;
                break;
            case ModelDownloadStatus.Cancelled:
                ShowSttDownloadLink = true;
                break;
        }
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
        try
        {
            return ModelSelectionPolicy.SelectTranslationProfile(
                _modelCatalog,
                _activeModelId,
                source,
                target,
                _routingMode,
                _routeUnsupportedPairsToCloud,
                _cloudPreferences,
                _cloudProviderRuntimeState.GetRoutingState(_cloudPreferences)).Descriptor;
        }
        catch (Exception)
        {
            if (ModelSelectionPolicy.FindProfileById(_modelCatalog, _activeModelId, _cloudPreferences) is { } activeProfile)
            {
                return activeProfile.Descriptor;
            }

            if (!string.IsNullOrWhiteSpace(_activeModelId) && _modelManager is not null)
            {
                var installed = _modelManager.ListInstalled().FirstOrDefault(m => string.Equals(m.Id, _activeModelId, StringComparison.OrdinalIgnoreCase));
                if (installed is not null)
                    return new ModelDescriptor(installed.Id, installed.DisplayName, "", installed.SizeBytes, installed.Type);
            }

            return null;
        }
    }

    private string? ResolvePersistedActiveModelId(string sourceLanguage, string targetLanguage)
    {
        if (!string.IsNullOrWhiteSpace(_activeModelId))
            return _activeModelId;

        try
        {
            return ModelSelectionPolicy.SelectTranslationProfile(
                _modelCatalog,
                null,
                sourceLanguage,
                targetLanguage,
                TranslationRoutingMode.LocalOnly,
                routeUnsupportedPairsToCloud: false).Id;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void ApplyRoutingSettings(SettingsModel settings)
    {
        _routingMode = ParseRoutingMode(settings.Translation.ModelPolicy.RoutingMode);
        _routeUnsupportedPairsToCloud = settings.Translation.ModelPolicy.RouteUnsupportedPairsToCloud;
        _cloudPreferences = new CloudModelPreferences(
            settings.Translation.CloudProvider.Enabled,
            settings.Translation.CloudProvider.BaseUrl,
            settings.Translation.CloudProvider.ApiKey,
            settings.Translation.CloudProvider.TranslationModelId,
            settings.Translation.CloudProvider.PostProcessingModelId);
    }

    private static string? GetPreferredLocalModelId(SettingsModel settings) =>
        string.IsNullOrWhiteSpace(settings.Translation.ModelPolicy.PreferredLocalTranslationModelId)
            ? settings.Translation.ActiveTranslationModelId
            : settings.Translation.ModelPolicy.PreferredLocalTranslationModelId;

    private static TranslationRoutingMode ParseRoutingMode(string? routingMode) =>
        Enum.TryParse<TranslationRoutingMode>(routingMode, ignoreCase: true, out var parsed)
            ? parsed
            : TranslationRoutingMode.PreferLocal;

    private static string BuildCloudPreferencesSignature(CloudModelPreferences preferences) =>
        string.Join("|",
            preferences.Enabled ? "1" : "0",
            preferences.BaseUrl ?? string.Empty,
            preferences.ApiKey ?? string.Empty,
            preferences.TranslationModelId ?? string.Empty,
            preferences.PostProcessingModelId ?? string.Empty);

    public void Dispose()
    {
        lock (_pipelineCtsLock)
        {
            if (_disposed) return;
            _disposed = true;
            // Cancel under the lock so the owning task's Dispose() stays
            // serialized with this call. The task's finally-block disposes
            // the CTS; we must not do it here or we would race with it.
            _pipelineCts?.Cancel();
        }

        if (_speechCoordinator is not null)
        {
            _speechCoordinator.StateChanged -= HandleVoiceStateChanged;
            _speechCoordinator.PartialTranscription -= HandlePartialTranscription;
        }

        if (_onDownloadStateChanged is not null)
            _downloadCoordinator.StateChanged -= _onDownloadStateChanged;

        _messenger.Unregister<SettingsChangedMessage>(this);

        GC.SuppressFinalize(this);
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
