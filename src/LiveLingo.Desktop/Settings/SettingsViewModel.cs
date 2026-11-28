using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Core.LocalHost;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LocalHost;
using LiveLingo.Desktop.Services.Preferences;

namespace LiveLingo.Desktop.Settings;

/// <summary>One line of the glossary list; a term whose translation equals it is kept as written.</summary>
public sealed record TermEntry(string Source, string Target);

/// <summary>The stages that can run on the user's own cloud endpoint, each authorized on its own.</summary>
public enum CloudStep
{
    Translate,
    Clean,
    Speech
}

/// <summary>One saved cloud endpoint as the page shows it; the API key is never part of it, only whether one is stored.</summary>
public sealed record EndpointEntry(
    string Id,
    string Name,
    string BaseUrl,
    string Model,
    bool HasKey,
    bool Usable,
    bool UsedForTranslate,
    bool UsedForClean,
    bool UsedForSpeech = false,
    bool SpeechUsable = false);

/// <summary>Why the add or edit form was not accepted; the page shows the reason beside the field.</summary>
public enum EndpointError
{
    NameMissing,
    NameTaken,
    UrlInvalid,
    ModelMissing,
    KeyMissing
}

/// <summary>The form that adds an endpoint (<paramref name="Id"/> null) or edits one, and why its last submission was refused.</summary>
public sealed record EndpointForm(string? Id, EndpointError? Error);

/// <summary>Why the polish preset add or edit form was refused.</summary>
public enum PresetFormError
{
    NameMissing,
    NameTaken,
    DescriptionInvalid,
    InstructionsInvalid
}

/// <summary>The open modal form for adding a custom polish preset (<paramref name="Id"/> null) or editing an existing preset.</summary>
public sealed record PresetForm(string? Id, PresetFormError? Error);

/// <summary>A stage routing request to a cloud endpoint waiting for the user's explicit privacy consent.</summary>
public sealed record PendingCloudConsent(CloudStep Step, string EndpointId, string? PreviousEndpointId);

/// <summary>
/// The settings page of the prototype: there is no save button, every change is applied at once. A value that is not
/// usable yet (a hotkey being typed, a half-filled cloud endpoint) is flagged and not stored.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    public static readonly IReadOnlyList<string> Languages = ["zh-CN", "en-US"];
    public static readonly IReadOnlyList<string> CommitKeys = [nameof(CommitKey.Enter), nameof(CommitKey.CtrlEnter)];
    public static readonly IReadOnlyList<string> Themes = ["Auto", "Light", "Dark"];
    public static readonly IReadOnlyList<string> Targets = [nameof(TargetLanguage.English), nameof(TargetLanguage.Chinese)];
    public static readonly IReadOnlyList<string> DefaultOutputs = [nameof(ComposerOutput.Clean), nameof(ComposerOutput.Translate), nameof(ComposerOutput.Original)];

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IModelManager _models;
    private readonly RecentDrafts _recent;
    private readonly IMicrophone _microphone;
    private readonly ComposerPreferences _preferences;
    private readonly bool _ownsCoordinator;
    private IReadOnlyList<string> _legacyIds = [];
    private bool _loading = true;
    private bool _devicesLoaded;

    public SettingsViewModel(
        ISettingsService settings,
        ISecretStore secrets,
        IModelManager models,
        RecentDrafts recent,
        IMicrophone microphone,
        ComposerPreferences preferences,
        ModelSelectionCoordinator? modelCoordinator = null,
        LocalHostCoordinator? localHostCoordinator = null)
    {
        _settings = settings;
        _secrets = secrets;
        _models = models;
        _recent = recent;
        _microphone = microphone;
        _preferences = preferences;
        _ownsCoordinator = modelCoordinator is null;
        ModelCoordinator = modelCoordinator ?? new ModelSelectionCoordinator(
            settings, models, new InProcessModelDownloadCoordinator(models));
        ModelCoordinator.Changed += OnModelsChanged;
        LocalHostCoordinator = localHostCoordinator;
        if (LocalHostCoordinator is not null)
            LocalHostCoordinator.Changed += OnModelsChanged;

        var current = settings.CloneCurrent();
        _summonHotkey = current.Hotkeys.Summon;
        _commitKeyIndex = Math.Max(0, CommitKeys.ToList().IndexOf(current.Composer.CommitKey));
        _languageIndex = Math.Max(0, Languages.ToList().IndexOf(current.Appearance.Language));
        _themeIndex = Math.Max(0, Themes.ToList().IndexOf(current.Appearance.Theme));
        _reduceMotion = !current.Appearance.Animations;
        _targetIndex = Math.Max(0, Targets.ToList().IndexOf(current.Composer.TargetLanguage));
        _cleanupEnabled = current.Composer.CleanupEnabled;
        _defaultOutputIndex = Math.Max(0, DefaultOutputs.ToList().IndexOf(current.Composer.DefaultOutput));
        _autoDictate = current.Composer.AutoDictate;
        _preloadSpeech = current.Composer.PreloadSpeech;
        _builtInGlossary = current.Composer.BuiltInGlossary;
        _rememberChoices = current.Composer.RememberChoices;
        _defaultPolishMode = current.Composer.ResolvedDefaultPolishMode();
        _smartHostPolish = current.Composer.SmartHostPolish;
        var naturalPreset = current.Composer.EffectivePolishPresets().First(p => p.Id == PolishPresets.NaturalId);
        _polishPrompt = naturalPreset.IsCustomized ? naturalPreset.CustomInstructions : "";
        _microphoneNames = [""];
        _microphoneIndex = 0;
        _speechEndpointId = current.Cloud.SpeechEndpointId;
        _translatorEndpointId = current.Cloud.TranslatorEndpointId;
        _cleanerEndpointId = current.Cloud.CleanerEndpointId;
        _endpoints = EntriesOf(current.Cloud);
        var (parsedTerms, parsedCorrections) = ParseGlossaryEntries(current.Composer.Glossary);
        Terms = new ObservableCollection<TermEntry>(parsedTerms);
        Corrections = new ObservableCollection<GlossaryCorrection>(parsedCorrections);
        _loading = false;
    }

    // ---- general -------------------------------------------------------------------

    [ObservableProperty] private string _summonHotkey;

    /// <summary>True when the hotkey text is not a valid shortcut; the window shows the localized message.</summary>
    [ObservableProperty] private bool _hotkeyInvalid;

    [ObservableProperty] private int _commitKeyIndex;
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private bool _reduceMotion;

    // ---- speech and translation ----------------------------------------------------------

    /// <summary>The device names after "system default" (an empty name).</summary>
    [ObservableProperty] private IReadOnlyList<string> _microphoneNames;

    [ObservableProperty] private int _microphoneIndex;
    [ObservableProperty] private int _targetIndex;

    /// <summary>Whether dictated and typed sentences are cleaned up at all; off offers only the original wording.</summary>
    [ObservableProperty] private bool _cleanupEnabled;

    /// <summary>The output a composer begins with where nothing is remembered; an index into <see cref="DefaultOutputs"/>.</summary>
    [ObservableProperty] private int _defaultOutputIndex;

    /// <summary>Whether the microphone starts listening as soon as the composer opens.</summary>
    [ObservableProperty] private bool _autoDictate;

    /// <summary>Whether the speech models are loaded when the application starts (see <c>ModelWarmup</c>).</summary>
    [ObservableProperty] private bool _preloadSpeech;

    /// <summary>Whether LiveLingo's built-in bilingual developer glossary is active alongside the user's terms.</summary>
    [ObservableProperty] private bool _builtInGlossary;

    /// <summary>Whether the output and direction last chosen for an application are used the next time it is the target.</summary>
    [ObservableProperty] private bool _rememberChoices;

    [ObservableProperty] private string _defaultPolishMode = PolishPresets.NaturalId;

    [ObservableProperty] private bool _smartHostPolish = true;

    [ObservableProperty] private PresetForm? _activePresetForm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingDeletePreset))]
    private string? _pendingDeletePresetId;

    public IReadOnlyList<PolishPreset> Presets => _settings.Current.Composer.EffectivePolishPresets();

    public PolishPreset? PendingDeletePreset =>
        PendingDeletePresetId is null ? null : Presets.FirstOrDefault(p => p.Id == PendingDeletePresetId && !p.BuiltIn);

    /// <summary>
    /// The polish requirements as the user wrote them for the default <c>natural</c> mode, "" for the built-in ones.
    /// </summary>
    [ObservableProperty] private string _polishPrompt = "";

    [ObservableProperty] private bool _polishPromptInvalid;

    /// <summary>What the page shows in the box: the built-in requirements where there are no custom ones, to start editing from.</summary>
    public string PolishPromptShown => PolishPromptIsDefault ? PolishStyle.Default : PolishPrompt;

    /// <summary>True while the built-in requirements are used.</summary>
    public bool PolishPromptIsDefault => !PolishPromptInvalid && PolishStyle.ToStored(PolishPrompt) == "";

    /// <summary>What is remembered per application, the one chosen in most recently first.</summary>
    public IReadOnlyList<AppChoice> Apps => _preferences.Apps;

    public void ForgetApp(string appId)
    {
        _preferences.Forget(appId);
        OnPropertyChanged(nameof(Apps));
    }

    public void SetAppPreference(string appId, string appName, string encodedChoice)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return;

        if (string.Equals(encodedChoice, "default", StringComparison.Ordinal))
        {
            _preferences.Forget(appId);
            OnPropertyChanged(nameof(Apps));
            return;
        }

        if (encodedChoice.StartsWith("translate:", StringComparison.Ordinal))
        {
            var langPart = encodedChoice["translate:".Length..];
            if (Enum.TryParse<TargetLanguage>(langPart, ignoreCase: true, out var target))
            {
                _preferences.SetAppChoice(appId, appName, ComposerOutput.Translate, target, DefaultPolishMode);
                OnPropertyChanged(nameof(Apps));
            }
            return;
        }

        if (encodedChoice.StartsWith("clean:", StringComparison.Ordinal))
        {
            var modeId = encodedChoice["clean:".Length..];
            if (Presets.Any(p => p.Enabled && string.Equals(p.Id, modeId, StringComparison.Ordinal)))
            {
                var defaultTarget = Enum.Parse<TargetLanguage>(_settings.Current.Composer.TargetLanguage);
                _preferences.SetAppChoice(appId, appName, ComposerOutput.Clean, defaultTarget, modeId);
                OnPropertyChanged(nameof(Apps));
            }
        }
    }

    public void ClearApps()
    {
        _preferences.Clear();
        OnPropertyChanged(nameof(Apps));
    }

    // ---- scenario polish presets -----------------------------------------------------------

    public void OpenPresetForm(string? id)
    {
        if (id is null || Presets.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal)))
            ActivePresetForm = new PresetForm(id, null);
    }

    public void ClosePresetForm() => ActivePresetForm = null;

    public bool SavePreset(string name, string description, string gate, string instructions)
    {
        if (ActivePresetForm is not { } form)
            return false;

        var current = Presets;
        var existing = form.Id is null ? null : current.FirstOrDefault(p => string.Equals(p.Id, form.Id, StringComparison.Ordinal));
        if (form.Id is not null && existing is null)
        {
            ActivePresetForm = null;
            return false;
        }

        var builtIn = existing?.BuiltIn == true;
        var trimmedName = (name ?? "").Trim();
        var trimmedDesc = (description ?? "").Trim();
        var storedInstructions = PolishStyle.ToStored(instructions ?? "");

        if (!builtIn && !PolishPresets.IsValidName(trimmedName))
        {
            ActivePresetForm = form with { Error = PresetFormError.NameMissing };
            return false;
        }
        if (!builtIn && current.Any(p => p.Id != existing?.Id && string.Equals(p.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            ActivePresetForm = form with { Error = PresetFormError.NameTaken };
            return false;
        }
        if (!builtIn && !PolishPresets.IsValidDescription(trimmedDesc))
        {
            ActivePresetForm = form with { Error = PresetFormError.DescriptionInvalid };
            return false;
        }
        if (storedInstructions is null || (!builtIn && storedInstructions.Length == 0))
        {
            ActivePresetForm = form with { Error = PresetFormError.InstructionsInvalid };
            return false;
        }

        var model = _settings.CloneCurrent();
        if (builtIn)
        {
            var builtInId = existing!.Id;
            var normalized = PolishStyle.NormalizeText(instructions ?? "");
            var isBuiltInDefault = string.IsNullOrWhiteSpace(instructions)
                || normalized == PolishStyle.NormalizeText(existing.DefaultInstructions);
            var entry = model.Composer.PolishPresets.FirstOrDefault(e => e.Id == builtInId);
            if (entry is null && !isBuiltInDefault)
            {
                model.Composer.PolishPresets.Add(new PolishPresetEntry
                {
                    Id = builtInId,
                    Enabled = existing.Enabled,
                    Gate = existing.Gate.ToString(),
                    Instructions = normalized
                });
            }
            else if (entry is not null)
            {
                entry.Instructions = isBuiltInDefault ? "" : normalized;
            }
            if (builtInId == PolishPresets.NaturalId)
            {
                model.Composer.PolishPrompt = isBuiltInDefault ? "" : normalized;
                _loading = true;
                PolishPrompt = isBuiltInDefault ? "" : normalized;
                PolishPromptInvalid = false;
                _loading = false;
            }
        }
        else if (existing is not null)
        {
            var entry = model.Composer.PolishPresets.First(e => e.Id == existing.Id);
            entry.Name = trimmedName;
            entry.Description = trimmedDesc;
            entry.Gate = Enum.TryParse<PolishGateKind>(gate, ignoreCase: true, out var g) ? g.ToString() : nameof(PolishGateKind.FactAnchor);
            entry.Instructions = PolishStyle.NormalizeText(instructions!);
        }
        else
        {
            var parsedGate = Enum.TryParse<PolishGateKind>(gate, ignoreCase: true, out var g) ? g : PolishGateKind.FactAnchor;
            model.Composer.PolishPresets.Add(new PolishPresetEntry
            {
                Id = "custom-" + Guid.NewGuid().ToString("N")[..12],
                Name = trimmedName,
                Description = trimmedDesc,
                Gate = parsedGate.ToString(),
                Enabled = true,
                Instructions = PolishStyle.NormalizeText(instructions!)
            });
        }

        _settings.Replace(model);
        ActivePresetForm = null;
        OnPropertyChanged(nameof(Presets));
        return true;
    }

    public void ResetBuiltInPreset(string id)
    {
        if (!PolishPresets.IsBuiltInId(id))
            return;

        var model = _settings.CloneCurrent();
        var entry = model.Composer.PolishPresets.FirstOrDefault(e => e.Id == id);
        if (entry is not null)
            entry.Instructions = "";
        if (id == PolishPresets.NaturalId)
        {
            model.Composer.PolishPrompt = "";
            _loading = true;
            PolishPrompt = "";
            PolishPromptInvalid = false;
            _loading = false;
        }
        _settings.Replace(model);
        OnPropertyChanged(nameof(Presets));
    }

    public void SetPresetEnabled(string id, bool enabled)
    {
        if (string.Equals(id, DefaultPolishMode, StringComparison.Ordinal) && !enabled)
            return;

        var existing = Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (existing is null)
            return;

        var model = _settings.CloneCurrent();
        var entry = model.Composer.PolishPresets.FirstOrDefault(e => e.Id == id);
        if (entry is null && existing.BuiltIn)
        {
            model.Composer.PolishPresets.Add(new PolishPresetEntry
            {
                Id = id,
                Enabled = enabled,
                Gate = existing.Gate.ToString(),
                Instructions = existing.IsCustomized ? existing.CustomInstructions : ""
            });
        }
        else if (entry is not null)
        {
            entry.Enabled = enabled;
        }
        _settings.Replace(model);
        OnPropertyChanged(nameof(Presets));
    }

    public void DeletePreset(string id)
    {
        if (Presets.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal) && !p.BuiltIn))
            PendingDeletePresetId = id;
    }

    public void AnswerDeletePreset(bool allow)
    {
        var id = PendingDeletePresetId;
        PendingDeletePresetId = null;
        if (!allow || id is null)
            return;

        var model = _settings.CloneCurrent();
        if (model.Composer.PolishPresets.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal)) == 0)
            return;

        if (string.Equals(model.Composer.DefaultPolishMode, id, StringComparison.Ordinal))
        {
            model.Composer.DefaultPolishMode = PolishPresets.NaturalId;
            _loading = true;
            DefaultPolishMode = PolishPresets.NaturalId;
            _loading = false;
        }
        _settings.Replace(model);
        _preferences.ForgetPolishMode(id);
        OnPropertyChanged(nameof(Presets));
        OnPropertyChanged(nameof(Apps));
    }

    public ObservableCollection<TermEntry> Terms { get; }

    [ObservableProperty] private string _newTermSource = "";
    [ObservableProperty] private string _newTermTarget = "";

    public ObservableCollection<GlossaryCorrection> Corrections { get; }

    [ObservableProperty] private string _newCorrectionSpoken = "";
    [ObservableProperty] private string _newCorrectionCanonical = "";

    // ---- privacy and cloud -----------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudSpeech), nameof(SpeechEndpoint))]
    private string _speechEndpointId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudTranslate), nameof(TranslatorEndpoint))]
    private string _translatorEndpointId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudClean), nameof(CleanerEndpoint))]
    private string _cleanerEndpointId;

    public bool CloudSpeech => !string.IsNullOrEmpty(SpeechEndpointId);

    public bool CloudTranslate => !string.IsNullOrEmpty(TranslatorEndpointId);

    public bool CloudClean => !string.IsNullOrEmpty(CleanerEndpointId);

    /// <summary>True when a cloud selection was asked for on an endpoint that is missing or unusable (no key, bad address).</summary>
    [ObservableProperty] private bool _cloudInvalid;

    /// <summary>The saved endpoints, in the order they were added.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeechEndpoint), nameof(TranslatorEndpoint), nameof(CleanerEndpoint), nameof(PendingDeleteEndpoint))]
    private IReadOnlyList<EndpointEntry> _endpoints;

    public EndpointEntry? SpeechEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == SpeechEndpointId);

    public EndpointEntry? TranslatorEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == TranslatorEndpointId);

    public EndpointEntry? CleanerEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == CleanerEndpointId);

    /// <summary>The add or edit form while it is open; the page draws it over the settings.</summary>
    [ObservableProperty] private EndpointForm? _form;

    /// <summary>An endpoint deletion waiting for the user's confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingDeleteEndpoint))]
    private string? _pendingDeleteId;

    public EndpointEntry? PendingDeleteEndpoint =>
        PendingDeleteId is null ? null : Endpoints.FirstOrDefault(endpoint => endpoint.Id == PendingDeleteId);

    // Endpoints whose API key is in the secret store; read when the window opens and kept up to date by the commands below.
    private readonly HashSet<string> _keyed = [];

    // ---- models & local hosts --------------------------------------------------------------

    public ModelSelectionCoordinator ModelCoordinator { get; }

    public LocalHostCoordinator? LocalHostCoordinator { get; }

    public HardwareProfile Hardware => ModelCoordinator.Hardware;

    public ModelSelection ActiveModels => ModelCoordinator.ActiveSelection;

    public LocalHostStageBinding SpeechLocalHost => _settings.Current.LocalHost.Speech;

    public LocalHostStageBinding CleanerLocalHost => _settings.Current.LocalHost.Cleaner;

    public LocalHostStageBinding TranslatorLocalHost => _settings.Current.LocalHost.Translator;

    public bool LocalHostSpeech => SpeechLocalHost.IsBound;

    public bool LocalHostClean => CleanerLocalHost.IsBound;

    public bool LocalHostTranslate => TranslatorLocalHost.IsBound;

    public bool AnyLocalHost => LocalHostSpeech || LocalHostClean || LocalHostTranslate;

    public IReadOnlyList<LocalHostInstance> DiscoveredLocalHosts => LocalHostCoordinator?.Hosts ?? [];

    public IReadOnlyList<LocalHostDownloadJob> LocalHostDownloads => LocalHostCoordinator?.ActiveDownloads ?? [];

    public bool IsRefreshingLocalHosts => LocalHostCoordinator?.IsRefreshing == true;

    /// <summary>Raised when model selection, local host discovery, download progress or disk state changes.</summary>
    public event Action? ModelsChanged;

    public bool SelectModel(string modelId) => ModelCoordinator.SelectModel(modelId);

    public bool DownloadModel(string modelId) => ModelCoordinator.RetryArtifactDownload(modelId);

    public void CancelModelDownload(string modelId) => ModelCoordinator.CancelDownload(modelId);

    public Task<bool> DeleteModelAsync(string modelId) => ModelCoordinator.DeleteModelAsync(modelId);

    public void ApplyRecommendedModels() => ModelCoordinator.ApplyRecommended();

    /// <summary>Explicitly routes <paramref name="stage"/> to a discovered local model host (Bionic, LM Studio, Ollama, or local OpenAI-compatible ASR/LLM server).</summary>
    public bool SelectLocalHostModel(ModelStage stage, string hostId, string modelKey)
    {
        if (stage is not (ModelStage.Speech or ModelStage.Cleaner or ModelStage.Translator)
            || string.IsNullOrWhiteSpace(hostId)
            || string.IsNullOrWhiteSpace(modelKey))
        {
            return false;
        }

        var host = LocalHostCoordinator?.FindHost(hostId);
        if (stage == ModelStage.Speech)
        {
            var kind = host?.Kind ?? InferHostKind(hostId);
            if (!LocalHostRoute.SupportsHttpSpeech(kind))
                return false;
        }

        var baseUrl = host?.BaseUri?.ToString() ?? "";
        if (string.IsNullOrEmpty(baseUrl))
        {
            var current = _settings.Current.LocalHost;
            baseUrl = current.PortOverrides.TryGetValue(hostId, out var overridden)
                ? overridden
                : current.CustomHosts.FirstOrDefault(c => string.Equals(c.Id, hostId, StringComparison.OrdinalIgnoreCase))?.BaseUrl ?? "";
        }

        ModelCoordinator.ClearPendingActivation(stage);
        var model = _settings.CloneCurrent();
        var binding = new LocalHostStageBinding { HostId = hostId.Trim(), BaseUrl = baseUrl, ModelKey = modelKey.Trim() };
        if (stage == ModelStage.Speech)
        {
            model.Cloud.SpeechEndpointId = "";
            model.LocalHost.Speech = binding;
        }
        else if (stage == ModelStage.Cleaner)
        {
            model.Cloud.CleanerEndpointId = "";
            model.LocalHost.Cleaner = binding;
        }
        else
        {
            model.Cloud.TranslatorEndpointId = "";
            model.LocalHost.Translator = binding;
        }

        _settings.Replace(model);
        SyncCloudSnapshot(model.Cloud);
        ModelsChanged?.Invoke();
        return true;
    }

    /// <summary>Binds both cleanup and translation stages to one already-loaded hot model on a shared local host.</summary>
    public bool ApplyHotLocalHostModel(string hostId, string modelKey)
    {
        if (string.IsNullOrWhiteSpace(hostId) || string.IsNullOrWhiteSpace(modelKey))
            return false;

        var baseUrl = LocalHostCoordinator?.FindHost(hostId)?.BaseUri?.ToString() ?? "";
        ModelCoordinator.ClearPendingActivation(ModelStage.Cleaner);
        ModelCoordinator.ClearPendingActivation(ModelStage.Translator);

        var model = _settings.CloneCurrent();
        var binding = new LocalHostStageBinding { HostId = hostId.Trim(), BaseUrl = baseUrl, ModelKey = modelKey.Trim() };
        model.Cloud.CleanerEndpointId = "";
        model.Cloud.TranslatorEndpointId = "";
        model.LocalHost.Cleaner = binding.DeepClone();
        model.LocalHost.Translator = binding.DeepClone();
        _settings.Replace(model);
        SyncCloudSnapshot(model.Cloud);
        ModelsChanged?.Invoke();
        return true;
    }

    public Task RefreshLocalHostsAsync() =>
        LocalHostCoordinator?.RefreshAsync(force: true) ?? Task.CompletedTask;

    private static LocalHostKind InferHostKind(string hostId) => hostId.Trim().ToLowerInvariant() switch
    {
        LocalHostEnvironmentProbe.BionicHostId => LocalHostKind.Bionic,
        LocalHostEnvironmentProbe.LmStudioHostId => LocalHostKind.LmStudio,
        LocalHostEnvironmentProbe.OllamaHostId => LocalHostKind.Ollama,
        _ => LocalHostKind.OpenAiLocal
    };

    public Task<bool> DownloadLocalHostModelAsync(string hostId, ModelStage stage, string catalogModelId) =>
        LocalHostCoordinator?.StartDownloadAsync(hostId, stage, catalogModelId, Hardware) ?? Task.FromResult(false);

    public Task<bool> StartLocalHostServerAsync(string hostId) =>
        LocalHostCoordinator?.TryStartLocalServerAsync(hostId) ?? Task.FromResult(false);

    public bool OpenLocalHostApp(string hostId) =>
        LocalHostCoordinator?.TryOpenHostApp(hostId) == true;

    public async Task<bool> SaveLocalHostConfigAsync(string hostId, string baseUrl, string apiToken, bool clearToken)
    {
        if (string.IsNullOrWhiteSpace(hostId))
            return false;

        var trimmedUrl = (baseUrl ?? "").Trim();
        if (trimmedUrl.Length > 0 && !LocalHostRoute.TryParseLoopbackUri(trimmedUrl, out _))
            return false;

        var model = _settings.CloneCurrent();
        if (trimmedUrl.Length > 0)
            model.LocalHost.PortOverrides[hostId.Trim()] = trimmedUrl.TrimEnd('/');
        else
            model.LocalHost.PortOverrides.Remove(hostId.Trim());
        _settings.Replace(model);

        if (LocalHostCoordinator is not null)
        {
            if (clearToken)
                await LocalHostCoordinator.SaveTokenAsync(hostId.Trim(), "").ConfigureAwait(true);
            else if (!string.IsNullOrWhiteSpace(apiToken))
                await LocalHostCoordinator.SaveTokenAsync(hostId.Trim(), apiToken.Trim()).ConfigureAwait(true);
            else
                await LocalHostCoordinator.RefreshAsync(force: true).ConfigureAwait(true);
        }
        ModelsChanged?.Invoke();
        return true;
    }

    public async Task<bool> AddCustomLocalHostAsync(string name, string baseUrl)
    {
        if (!LocalHostRoute.TryParseLoopbackUri(baseUrl, out var uri) || uri is null)
            return false;

        var model = _settings.CloneCurrent();
        var id = CustomLocalHostEntry.NewId();
        var display = string.IsNullOrWhiteSpace(name) ? $"Local ({uri.Authority})" : name.Trim();
        model.LocalHost.CustomHosts.Add(new CustomLocalHostEntry
        {
            Id = id,
            Name = display,
            BaseUrl = uri.ToString().TrimEnd('/')
        });
        _settings.Replace(model);
        if (LocalHostCoordinator is not null)
            await LocalHostCoordinator.RefreshAsync(force: true).ConfigureAwait(true);
        ModelsChanged?.Invoke();
        return true;
    }

    public async Task RemoveCustomLocalHostAsync(string hostId)
    {
        if (string.IsNullOrWhiteSpace(hostId))
            return;

        var model = _settings.CloneCurrent();
        var removed = model.LocalHost.CustomHosts.RemoveAll(c => string.Equals(c.Id, hostId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return;

        if (string.Equals(model.LocalHost.Speech.HostId, hostId, StringComparison.OrdinalIgnoreCase))
            model.LocalHost.Speech.Clear();
        if (string.Equals(model.LocalHost.Cleaner.HostId, hostId, StringComparison.OrdinalIgnoreCase))
            model.LocalHost.Cleaner.Clear();
        if (string.Equals(model.LocalHost.Translator.HostId, hostId, StringComparison.OrdinalIgnoreCase))
            model.LocalHost.Translator.Clear();
        _settings.Replace(model);
        await _secrets.DeleteSecretAsync(LocalHostSettings.TokenSecretIdFor(hostId)).ConfigureAwait(true);
        if (LocalHostCoordinator is not null)
            await LocalHostCoordinator.RefreshAsync(force: true).ConfigureAwait(true);
        ModelsChanged?.Invoke();
    }

    /// <summary>Models that earlier versions downloaded and this one no longer uses; they are only removed on request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLegacyModels))]
    private int _legacyCount;

    [ObservableProperty] private long _legacyBytes;

    public bool HasLegacyModels => LegacyCount > 0;

    public void Dispose()
    {
        ModelCoordinator.Changed -= OnModelsChanged;
        if (LocalHostCoordinator is not null)
            LocalHostCoordinator.Changed -= OnModelsChanged;
        if (_ownsCoordinator)
            ModelCoordinator.Dispose();
    }

    private void OnModelsChanged()
    {
        SyncCloudSnapshot(_settings.Current.Cloud);
        ModelsChanged?.Invoke();
    }

    // ---- applying --------------------------------------------------------------------------

    partial void OnSummonHotkeyChanged(string value) => Apply();
    partial void OnCommitKeyIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnLanguageIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnThemeIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnReduceMotionChanged(bool value) => Apply();
    partial void OnMicrophoneIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnTargetIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnCleanupEnabledChanged(bool value) => Apply();
    partial void OnDefaultOutputIndexChanged(int value) => ApplyIfSelected(value);
    partial void OnAutoDictateChanged(bool value) => Apply();
    partial void OnPreloadSpeechChanged(bool value) => Apply();
    partial void OnBuiltInGlossaryChanged(bool value) => Apply();
    partial void OnRememberChoicesChanged(bool value) => Apply();
    partial void OnDefaultPolishModeChanged(string value) => Apply();
    partial void OnSmartHostPolishChanged(bool value) => Apply();
    partial void OnPolishPromptChanged(string value) => Apply();

    /// <summary>A combo box reports -1 while its items are being replaced; that is not a choice.</summary>
    private void ApplyIfSelected(int index)
    {
        if (index >= 0)
            Apply();
    }

    /// <summary>Stores every usable value at once; the rest keeps its last stored state.</summary>
    private void Apply()
    {
        if (_loading)
            return;

        var model = _settings.CloneCurrent();
        if (IsValidHotkey(SummonHotkey))
        {
            model.Hotkeys.Summon = SummonHotkey.Trim();
            HotkeyInvalid = false;
        }
        else
        {
            HotkeyInvalid = true;
        }

        model.Composer.CommitKey = CommitKeys[CommitKeyIndex];
        model.Appearance.Language = Languages[LanguageIndex];
        model.Appearance.Theme = Themes[ThemeIndex];
        model.Appearance.Animations = !ReduceMotion;
        model.Composer.TargetLanguage = Targets[TargetIndex];
        model.Composer.CleanupEnabled = CleanupEnabled;
        model.Composer.DefaultOutput = DefaultOutputs[DefaultOutputIndex];
        model.Composer.AutoDictate = AutoDictate;
        model.Composer.PreloadSpeech = PreloadSpeech;
        model.Composer.BuiltInGlossary = BuiltInGlossary;
        model.Composer.RememberChoices = RememberChoices;
        if (model.Composer.EffectivePolishPresets().Any(p => string.Equals(p.Id, DefaultPolishMode, StringComparison.Ordinal)))
        {
            model.Composer.DefaultPolishMode = DefaultPolishMode;
            var defEntry = model.Composer.PolishPresets.FirstOrDefault(e => e.Id == DefaultPolishMode);
            if (defEntry is not null)
                defEntry.Enabled = true;
        }
        model.Composer.SmartHostPolish = SmartHostPolish;
        if (PolishStyle.ToStored(PolishPrompt) is { } polish)
        {
            model.Composer.PolishPrompt = polish;
            var naturalEntry = model.Composer.PolishPresets.FirstOrDefault(e => e.Id == PolishPresets.NaturalId);
            if (naturalEntry is null && polish.Length > 0)
            {
                model.Composer.PolishPresets.Add(new PolishPresetEntry
                {
                    Id = PolishPresets.NaturalId,
                    Enabled = true,
                    Gate = nameof(PolishGateKind.Verbatim),
                    Instructions = polish
                });
            }
            else if (naturalEntry is not null)
            {
                naturalEntry.Instructions = polish;
            }
            PolishPromptInvalid = false;
        }
        else
        {
            PolishPromptInvalid = true;
        }
        if (_devicesLoaded)
            model.Composer.Microphone = MicrophoneIndex < MicrophoneNames.Count ? MicrophoneNames[MicrophoneIndex] : "";
        model.Composer.Glossary = SerializeGlossary(Terms, Corrections);
        _settings.Replace(model);
    }

    private static bool IsValidHotkey(string gesture)
    {
        try
        {
            HotkeyParser.Parse("summon", gesture);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- glossary ----------------------------------------------------------------------------

    /// <summary>Adds a term; a missing translation means "keep as written". Returns false when there is no term.</summary>
    [RelayCommand]
    private void AddTerm()
    {
        var source = NewTermSource.Trim();
        if (source.Length == 0)
            return;

        var target = NewTermTarget.Trim();
        Terms.Add(new TermEntry(source, target.Length == 0 ? source : target));
        NewTermSource = "";
        NewTermTarget = "";
        Apply();
    }

    [RelayCommand]
    private void RemoveTerm(TermEntry term)
    {
        Terms.Remove(term);
        Apply();
    }

    [RelayCommand]
    private void AddCorrection()
    {
        var spoken = NewCorrectionSpoken.Trim();
        var canonical = NewCorrectionCanonical.Trim();
        if (spoken.Length == 0 || canonical.Length == 0 || string.Equals(spoken, canonical, StringComparison.Ordinal))
            return;

        var existing = Corrections.FirstOrDefault(c => string.Equals(c.Spoken, spoken, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Corrections[Corrections.IndexOf(existing)] = new GlossaryCorrection(spoken, canonical);
        else
            Corrections.Add(new GlossaryCorrection(spoken, canonical));
        NewCorrectionSpoken = "";
        NewCorrectionCanonical = "";
        Apply();
    }

    [RelayCommand]
    private void RemoveCorrection(GlossaryCorrection correction)
    {
        Corrections.Remove(correction);
        Apply();
    }

    private static (List<TermEntry> Terms, List<GlossaryCorrection> Corrections) ParseGlossaryEntries(string text)
    {
        var corrections = Glossary.Parse(text).Corrections.ToList();
        var nonCorrectionLines = string.Join('\n', (text ?? "")
            .Split('\n')
            .Select(line => line.TrimStart().StartsWith('#')
                ? ""
                : string.Join(',', line.Split([',', '，', ';', '；', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(entry => !entry.Contains("=>", StringComparison.Ordinal) && !entry.Contains('⇒')))));
        var termsOnly = Glossary.Parse(nonCorrectionLines);
        var pairs = termsOnly.Pairs.Select(pair => new TermEntry(pair.Source, pair.Target)).ToList();
        var pairSources = pairs.Select(pair => pair.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plain = termsOnly.Terms.Where(term => !pairSources.Contains(term)).Select(term => new TermEntry(term, term));
        return (plain.Concat(pairs).ToList(), corrections);
    }

    private static string SerializeGlossary(IEnumerable<TermEntry> terms, IEnumerable<GlossaryCorrection> corrections) =>
        string.Join(
            '\n',
            terms.Select(term => term.Source == term.Target ? term.Source : $"{term.Source}={term.Target}")
                .Concat(corrections.Select(c => $"{c.Spoken}=>{c.Canonical}")));

    // ---- cloud ------------------------------------------------------------------------------

    /// <summary>The cloud selection the user asked for and has not consented to yet; the page shows its consent dialog meanwhile.</summary>
    [ObservableProperty] private PendingCloudConsent? _pendingConsent;

    /// <summary>
    /// Selecting a cloud endpoint for a stage always asks for explicit privacy consent naming that endpoint and host
    /// (<see cref="AnswerConsent"/>), and refuses incomplete or keyless endpoints.
    /// </summary>
    public void SelectCloudEndpoint(CloudStep step, string endpointId)
    {
        if (Endpoints.FirstOrDefault(item => item.Id == endpointId) is not { } endpoint)
        {
            CloudInvalid = true;
            return;
        }

        var currentId = step switch
        {
            CloudStep.Speech => SpeechEndpointId,
            CloudStep.Translate => TranslatorEndpointId,
            _ => CleanerEndpointId
        };
        if (string.Equals(currentId, endpointId, StringComparison.Ordinal))
        {
            CloudInvalid = false;
            return;
        }

        var usable = step == CloudStep.Speech ? endpoint.SpeechUsable : endpoint.Usable;
        if (!usable)
        {
            CloudInvalid = true;
            return;
        }

        CloudInvalid = false;
        PendingConsent = new PendingCloudConsent(
            step,
            endpointId,
            currentId.Length > 0 ? currentId : null);
    }

    public void AnswerConsent(bool allow)
    {
        if (PendingConsent is not { } consent)
            return;

        PendingConsent = null;
        if (allow)
            TryApplyCloudEndpoint(consent.Step, consent.EndpointId);
    }

    /// <summary>Binds <paramref name="step"/> to <paramref name="endpointId"/> after explicit consent; refused when unusable.</summary>
    public bool TryApplyCloudEndpoint(CloudStep step, string endpointId)
    {
        if (Endpoints.FirstOrDefault(item => item.Id == endpointId) is not { } endpoint
            || !(step == CloudStep.Speech ? endpoint.SpeechUsable : endpoint.Usable))
        {
            CloudInvalid = true;
            return false;
        }

        var stage = step switch
        {
            CloudStep.Speech => ModelStage.Speech,
            CloudStep.Translate => ModelStage.Translator,
            _ => ModelStage.Cleaner
        };
        ModelCoordinator.ClearPendingActivation(stage);
        var model = _settings.CloneCurrent();
        switch (step)
        {
            case CloudStep.Speech:
                model.Cloud.SpeechEndpointId = endpointId;
                model.LocalHost.Speech.Clear();
                break;
            case CloudStep.Translate:
                model.Cloud.TranslatorEndpointId = endpointId;
                model.LocalHost.Translator.Clear();
                break;
            default:
                model.Cloud.CleanerEndpointId = endpointId;
                model.LocalHost.Cleaner.Clear();
                break;
        }
        _settings.Replace(model);
        SyncCloudSnapshot(model.Cloud);
        CloudInvalid = false;
        ModelsChanged?.Invoke();
        return true;
    }

    /// <summary>Immediately returns <paramref name="step"/> to its local model without touching saved cloud endpoints.</summary>
    public void SwitchStageToLocal(CloudStep step)
    {
        if (step == CloudStep.Speech && CloudSpeech)
            Mutate(cloud => cloud.SpeechEndpointId = "");
        else if (step == CloudStep.Translate && CloudTranslate)
            Mutate(cloud => cloud.TranslatorEndpointId = "");
        else if (step == CloudStep.Clean && CloudClean)
            Mutate(cloud => cloud.CleanerEndpointId = "");
    }

    /// <summary>Immediately switches every stage back to local offline models.</summary>
    public void DisableAllCloud()
    {
        if (!CloudSpeech && !CloudTranslate && !CloudClean)
            return;

        Mutate(cloud =>
        {
            cloud.SpeechEndpointId = "";
            cloud.TranslatorEndpointId = "";
            cloud.CleanerEndpointId = "";
        });
    }

    // ---- cloud endpoints --------------------------------------------------------------------
    // Any number of endpoints may be saved, and cleanup and translation independently pick whichever one they want (or stay
    // local). Anything that would move text to another service (selecting an endpoint, editing an active endpoint's host,
    // or deleting an endpoint) asks first or reverts that stage to local: text never moves without the user's say.

    /// <summary>Opens the form for a new endpoint (<paramref name="id"/> null) or to edit one.</summary>
    public void OpenEndpointForm(string? id)
    {
        if (id is null || Endpoints.Any(endpoint => endpoint.Id == id))
            Form = new EndpointForm(id, null);
    }

    public void CloseEndpointForm() => Form = null;

    /// <summary>
    /// Saves the open form. A rejected value keeps the form open with the reason; for an existing endpoint an empty key
    /// keeps the stored one. The key goes straight into the secret store and is never kept here.
    /// </summary>
    public async Task SaveEndpointAsync(string name, string baseUrl, string model, string apiKey)
    {
        if (Form is not { } form)
            return;

        var existing = form.Id is null ? null : Endpoints.FirstOrDefault(endpoint => endpoint.Id == form.Id);
        if (form.Id is not null && existing is null)
        {
            Form = null;
            return;
        }

        name = name.Trim();
        baseUrl = baseUrl.Trim().TrimEnd('/');
        model = model.Trim();
        apiKey = apiKey.Trim();

        var error = ValidateEndpoint(existing, name, baseUrl, model, apiKey);
        if (error is not null)
        {
            Form = form with { Error = error };
            return;
        }

        var id = existing?.Id ?? CloudEndpoint.NewId();
        var secretId = existing is null ? "cloud-key-" + id : _settings.CloneCurrent().Cloud.Endpoints.First(endpoint => endpoint.Id == id).SecretId;
        if (apiKey.Length > 0)
        {
            await _secrets.SetSecretAsync(secretId, apiKey).ConfigureAwait(true);
            _keyed.Add(id);
        }

        Mutate(cloud =>
        {
            var endpoint = cloud.Endpoints.FirstOrDefault(item => item.Id == id);
            if (endpoint is null)
            {
                endpoint = new CloudEndpoint { Id = id, SecretId = secretId };
                cloud.Endpoints.Add(endpoint);
            }
            else if (!SameService(endpoint.BaseUrl, baseUrl))
            {
                if (cloud.SpeechEndpointId == id)
                    cloud.SpeechEndpointId = "";
                if (cloud.TranslatorEndpointId == id)
                    cloud.TranslatorEndpointId = "";
                if (cloud.CleanerEndpointId == id)
                    cloud.CleanerEndpointId = "";
            }
            else if (!IsConfigured(baseUrl, model, hasKey: true))
            {
                if (cloud.TranslatorEndpointId == id)
                    cloud.TranslatorEndpointId = "";
                if (cloud.CleanerEndpointId == id)
                    cloud.CleanerEndpointId = "";
            }

            endpoint.Name = name;
            endpoint.BaseUrl = baseUrl;
            endpoint.Model = model;
        });
        Form = null;
    }

    private EndpointError? ValidateEndpoint(EndpointEntry? existing, string name, string baseUrl, string model, string apiKey)
    {
        if (name.Length == 0)
            return EndpointError.NameMissing;
        if (Endpoints.Any(endpoint => endpoint.Id != existing?.Id && string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase)))
            return EndpointError.NameTaken;
        if (!IsUsableUrl(baseUrl))
            return EndpointError.UrlInvalid;
        if (model.Length == 0)
            return EndpointError.ModelMissing;
        return apiKey.Length == 0 && existing?.HasKey != true ? EndpointError.KeyMissing : null;
    }

    /// <summary>Asks to delete an endpoint together with its API key; <see cref="AnswerDeleteEndpointAsync"/> carries it out.</summary>
    public void DeleteEndpoint(string id)
    {
        if (Endpoints.Any(endpoint => endpoint.Id == id))
            PendingDeleteId = id;
    }

    public async Task AnswerDeleteEndpointAsync(bool allow)
    {
        if (PendingDeleteId is not { } deleteId)
            return;

        PendingDeleteId = null;
        if (!allow || Endpoints.FirstOrDefault(endpoint => endpoint.Id == deleteId) is not { } endpoint)
            return;

        var secretId = _settings.CloneCurrent().Cloud.Endpoints.First(item => item.Id == endpoint.Id).SecretId;
        await _secrets.DeleteSecretAsync(secretId).ConfigureAwait(true);
        _keyed.Remove(endpoint.Id);
        Mutate(cloud =>
        {
            if (cloud.SpeechEndpointId == deleteId)
                cloud.SpeechEndpointId = "";
            if (cloud.TranslatorEndpointId == deleteId)
                cloud.TranslatorEndpointId = "";
            if (cloud.CleanerEndpointId == deleteId)
                cloud.CleanerEndpointId = "";
            cloud.Endpoints.RemoveAll(item => item.Id == deleteId);
        });
    }

    /// <summary>Changes the cloud settings and brings everything this page keeps of them up to date.</summary>
    private void Mutate(Action<CloudSettings> change)
    {
        var model = _settings.CloneCurrent();
        change(model.Cloud);
        _settings.Replace(model);
        SyncCloudSnapshot(model.Cloud);
        CloudInvalid = false;
    }

    private void SyncCloudSnapshot(CloudSettings cloud)
    {
        SpeechEndpointId = cloud.SpeechEndpointId;
        TranslatorEndpointId = cloud.TranslatorEndpointId;
        CleanerEndpointId = cloud.CleanerEndpointId;
        Endpoints = EntriesOf(cloud);
    }

    private IReadOnlyList<EndpointEntry> EntriesOf(CloudSettings cloud) =>
    [
        .. cloud.Endpoints.Select(endpoint =>
        {
            var hasKey = _keyed.Contains(endpoint.Id);
            return new EndpointEntry(
                endpoint.Id,
                endpoint.Name,
                endpoint.BaseUrl,
                endpoint.Model,
                hasKey,
                IsConfigured(endpoint.BaseUrl, endpoint.Model, hasKey),
                endpoint.Id == cloud.TranslatorEndpointId,
                endpoint.Id == cloud.CleanerEndpointId,
                endpoint.Id == cloud.SpeechEndpointId,
                IsSpeechConfigured(endpoint.BaseUrl, endpoint.Model, hasKey));
        })
    ];

    private static bool IsUsableUrl(string baseUrl)
    {
        try
        {
            SpeechCloudRoute.Create(baseUrl, "model", "key");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The key is checked for presence only; a stored key is validated when it is used.</summary>
    private static bool IsConfigured(string baseUrl, string model, bool hasKey)
    {
        try
        {
            CloudRoute.Create(baseUrl, model, hasKey ? "key" : "");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSpeechConfigured(string baseUrl, string model, bool hasKey)
    {
        try
        {
            SpeechCloudRoute.Create(baseUrl, model, hasKey ? "key" : "");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Whether two addresses reach the same service (scheme, host and port); the text goes to the same place.</summary>
    private static bool SameService(string before, string after) =>
        Uri.TryCreate(before, UriKind.Absolute, out var a) && Uri.TryCreate(after, UriKind.Absolute, out var b) &&
        string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    // ---- recent drafts and models -----------------------------------------------------------------

    [RelayCommand]
    private void ClearRecent() => _recent.Clear();

    /// <summary>Reads what the system provides (key, devices, models on disk); the window calls it when it opens.</summary>
    public async Task InitializeAsync()
    {
        foreach (var endpoint in _settings.CloneCurrent().Cloud.Endpoints)
        {
            if (!string.IsNullOrEmpty(await _secrets.GetSecretAsync(endpoint.SecretId).ConfigureAwait(true)))
                _keyed.Add(endpoint.Id);
        }
        SyncCloudSnapshot(_settings.CloneCurrent().Cloud);

        var saved = _settings.CloneCurrent().Composer.Microphone;
        var devices = await Task.Run(() => _microphone.InputDevices()).ConfigureAwait(true);
        _loading = true;
        MicrophoneNames = new[] { "" }.Concat(devices).ToList();
        MicrophoneIndex = Math.Max(0, MicrophoneNames.ToList().IndexOf(saved));
        _devicesLoaded = true;
        _loading = false;

        var installed = await Task.Run(() => _models.ListInstalled()).ConfigureAwait(true);
        ModelCoordinator.SyncInstalledSnapshot(installed);
        var current = ModelCatalog.All.Select(model => model.Id).ToHashSet();
        var legacy = installed.Where(model => !current.Contains(model.Id)).ToList();
        _legacyIds = legacy.Select(model => model.Id).ToList();
        LegacyBytes = legacy.Sum(model => model.SizeBytes);
        LegacyCount = legacy.Count;
        if (LocalHostCoordinator is not null)
            await LocalHostCoordinator.RefreshAsync(force: true).ConfigureAwait(true);
        ModelsChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RemoveLegacyModelsAsync()
    {
        await _models.CleanObsoleteModelsAsync(_legacyIds).ConfigureAwait(true);
        _legacyIds = [];
        LegacyCount = 0;
        LegacyBytes = 0;
    }
}
