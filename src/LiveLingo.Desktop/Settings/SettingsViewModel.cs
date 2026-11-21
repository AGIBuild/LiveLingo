using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
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
        ModelSelectionCoordinator? modelCoordinator = null)
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
        _polishPrompt = PolishStyle.IsUsable(current.Composer.PolishPrompt) ? current.Composer.PolishPrompt : "";
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

    /// <summary>
    /// The polish requirements as the user wrote them, "" for the built-in ones. Saved when set; a text that is not usable
    /// (<see cref="PolishStyle.IsUsable"/>) is flagged and not stored.
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

    public void ClearApps()
    {
        _preferences.Clear();
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

    // ---- models ----------------------------------------------------------------------------

    public ModelSelectionCoordinator ModelCoordinator { get; }

    public HardwareProfile Hardware => ModelCoordinator.Hardware;

    public ModelSelection ActiveModels => ModelCoordinator.ActiveSelection;

    /// <summary>Raised when model selection, download progress or disk state changes (possibly on a background thread).</summary>
    public event Action? ModelsChanged;

    public bool SelectModel(string modelId) => ModelCoordinator.SelectModel(modelId);

    public bool DownloadModel(string modelId) => ModelCoordinator.RetryArtifactDownload(modelId);

    public void CancelModelDownload(string modelId) => ModelCoordinator.CancelDownload(modelId);

    public Task<bool> DeleteModelAsync(string modelId) => ModelCoordinator.DeleteModelAsync(modelId);

    public void ApplyRecommendedModels() => ModelCoordinator.ApplyRecommended();

    /// <summary>Models that earlier versions downloaded and this one no longer uses; they are only removed on request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLegacyModels))]
    private int _legacyCount;

    [ObservableProperty] private long _legacyBytes;

    public bool HasLegacyModels => LegacyCount > 0;

    public void Dispose()
    {
        ModelCoordinator.Changed -= OnModelsChanged;
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
        if (PolishStyle.ToStored(PolishPrompt) is { } polish)
        {
            model.Composer.PolishPrompt = polish;
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
        Mutate(cloud =>
        {
            switch (step)
            {
                case CloudStep.Speech: cloud.SpeechEndpointId = endpointId; break;
                case CloudStep.Translate: cloud.TranslatorEndpointId = endpointId; break;
                default: cloud.CleanerEndpointId = endpointId; break;
            }
        });
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
