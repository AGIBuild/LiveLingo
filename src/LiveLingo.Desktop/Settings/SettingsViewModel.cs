using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Preferences;

namespace LiveLingo.Desktop.Settings;

/// <summary>One line of the glossary list; a term whose translation equals it is kept as written.</summary>
public sealed record TermEntry(string Source, string Target);

/// <summary>The two steps that can run on the user's own cloud endpoint, each authorized on its own.</summary>
public enum CloudStep
{
    Translate,
    Clean
}

/// <summary>One saved cloud endpoint as the page shows it; the API key is never part of it, only whether one is stored.</summary>
public sealed record EndpointEntry(string Id, string Name, string BaseUrl, string Model, bool HasKey, bool Active);

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

public enum EndpointChange
{
    Switch,
    Delete
}

/// <summary>A change of where text goes, or of what is stored, that waits for the user's answer.</summary>
public sealed record PendingEndpointChange(EndpointChange Kind, string Id);

/// <summary>
/// The settings page of the prototype: there is no save button, every change is applied at once. A value that is not
/// usable yet (a hotkey being typed, a half-filled cloud endpoint) is flagged and not stored.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Languages = ["zh-CN", "en-US"];
    public static readonly IReadOnlyList<string> CommitKeys = [nameof(CommitKey.Enter), nameof(CommitKey.CtrlEnter)];
    public static readonly IReadOnlyList<string> Themes = ["Auto", "Light", "Dark"];
    public static readonly IReadOnlyList<string> Targets = [nameof(TargetLanguage.English), nameof(TargetLanguage.Chinese)];
    public static readonly IReadOnlyList<string> Tiers = [nameof(TranslatorTier.Quality), nameof(TranslatorTier.Fast)];
    public static readonly IReadOnlyList<string> DefaultOutputs = [nameof(ComposerOutput.Clean), nameof(ComposerOutput.Translate), nameof(ComposerOutput.Original)];

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IModelManager _models;
    private readonly RecentDrafts _recent;
    private readonly IMicrophone _microphone;
    private readonly ComposerPreferences _preferences;
    private IReadOnlyList<string> _legacyIds = [];
    private bool _loading = true;
    private bool _devicesLoaded;

    public SettingsViewModel(
        ISettingsService settings, ISecretStore secrets, IModelManager models, RecentDrafts recent, IMicrophone microphone,
        ComposerPreferences preferences)
    {
        _settings = settings;
        _secrets = secrets;
        _models = models;
        _recent = recent;
        _microphone = microphone;
        _preferences = preferences;

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
        _rememberChoices = current.Composer.RememberChoices;
        _polishPrompt = PolishStyle.IsUsable(current.Composer.PolishPrompt) ? current.Composer.PolishPrompt : "";
        _tierIndex = Math.Max(0, Tiers.ToList().IndexOf(current.Composer.TranslatorTier));
        _microphoneNames = [""];
        _microphoneIndex = 0;
        _cloudTranslate = current.Cloud.Translate;
        _cloudClean = current.Cloud.Clean;
        _endpoints = EntriesOf(current.Cloud);
        Terms = new ObservableCollection<TermEntry>(ParseTerms(current.Composer.Glossary));
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

    // ---- privacy and cloud -----------------------------------------------------------------

    [ObservableProperty] private bool _cloudTranslate;
    [ObservableProperty] private bool _cloudClean;

    /// <summary>True when a cloud step was asked for but the enabled endpoint is missing or unusable (no key, bad address).</summary>
    [ObservableProperty] private bool _cloudInvalid;

    /// <summary>The saved endpoints, in the order they were added; at most one is the enabled one.</summary>
    [ObservableProperty] private IReadOnlyList<EndpointEntry> _endpoints;

    /// <summary>The endpoint the cloud steps use, or null while none is enabled.</summary>
    public EndpointEntry? ActiveEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Active);

    /// <summary>The add or edit form while it is open; the page draws it over the settings.</summary>
    [ObservableProperty] private EndpointForm? _form;

    /// <summary>A switch of the enabled endpoint or a deletion waiting for the user's answer.</summary>
    [ObservableProperty] private PendingEndpointChange? _pendingChange;

    // Endpoints whose API key is in the secret store; read when the window opens and kept up to date by the commands below.
    private readonly HashSet<string> _keyed = [];

    // ---- models ----------------------------------------------------------------------------

    [ObservableProperty] private int _tierIndex;

    /// <summary>Models that earlier versions downloaded and this one no longer uses; they are only removed on request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLegacyModels))]
    private int _legacyCount;

    [ObservableProperty] private long _legacyBytes;

    public bool HasLegacyModels => LegacyCount > 0;

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
    partial void OnRememberChoicesChanged(bool value) => Apply();
    partial void OnPolishPromptChanged(string value) => Apply();
    partial void OnTierIndexChanged(int value) => ApplyIfSelected(value);

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
        model.Composer.TranslatorTier = Tiers[TierIndex];
        if (_devicesLoaded)
            model.Composer.Microphone = MicrophoneIndex < MicrophoneNames.Count ? MicrophoneNames[MicrophoneIndex] : "";
        model.Composer.Glossary = SerializeTerms(Terms);
        model.Cloud.Translate = CloudTranslate;
        model.Cloud.Clean = CloudClean;
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

    private static IEnumerable<TermEntry> ParseTerms(string text)
    {
        var glossary = Glossary.Parse(text);
        var pairs = glossary.Pairs.Select(pair => new TermEntry(pair.Source, pair.Target)).ToList();
        var pairSources = pairs.Select(pair => pair.Source).ToHashSet();
        var plain = glossary.Terms.Where(term => !pairSources.Contains(term)).Select(term => new TermEntry(term, term));
        return plain.Concat(pairs);
    }

    private static string SerializeTerms(IEnumerable<TermEntry> terms) =>
        string.Join('\n', terms.Select(term => term.Source == term.Target ? term.Source : $"{term.Source}={term.Target}"));

    // ---- cloud ------------------------------------------------------------------------------

    /// <summary>The step the user asked to turn on and has not consented to yet; the page shows its consent dialog meanwhile.</summary>
    [ObservableProperty] private CloudStep? _pendingConsent;

    /// <summary>
    /// Turning a step on needs the user's consent every time (<see cref="AnswerConsent"/>) and an enabled, usable endpoint
    /// to send to; turning it off is immediate.
    /// </summary>
    public void SetCloud(CloudStep step, bool on)
    {
        var translation = step == CloudStep.Translate;
        if (!on)
            DisableCloud(translation);
        else if (!(translation ? CloudTranslate : CloudClean))
        {
            if (IsConfigured(ActiveEndpoint))
                PendingConsent = step;
            else
                CloudInvalid = true;
        }
    }

    public void AnswerConsent(bool allow)
    {
        if (PendingConsent is not { } step)
            return;

        PendingConsent = null;
        if (allow)
            TryEnableCloud(step == CloudStep.Translate);
    }

    /// <summary>Turns a cloud step on after the user consented; refused while no usable endpoint is enabled.</summary>
    public bool TryEnableCloud(bool translation)
    {
        if (!IsConfigured(ActiveEndpoint))
        {
            CloudInvalid = true;
            return false;
        }

        if (translation)
            CloudTranslate = true;
        else
            CloudClean = true;
        Apply();
        return true;
    }

    public void DisableCloud(bool translation)
    {
        if (translation)
            CloudTranslate = false;
        else
            CloudClean = false;
        Apply();
    }

    // ---- cloud endpoints --------------------------------------------------------------------
    // Any number of endpoints may be saved, at most one is enabled and both cloud steps use it. Whatever would change where
    // text goes (enabling another endpoint while a step is on, deleting, a new host for the enabled one) asks first or
    // switches the steps off: text never moves to another service without the user's say.

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
            else if (id == cloud.ActiveEndpointId && !SameService(endpoint.BaseUrl, baseUrl))
            {
                cloud.Translate = false;
                cloud.Clean = false;
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

    /// <summary>Enables an endpoint (the one that was enabled is switched off) or switches the enabled one off, which also turns both cloud steps off.</summary>
    public void SetEndpointEnabled(string id, bool on)
    {
        if (Endpoints.FirstOrDefault(endpoint => endpoint.Id == id) is not { } endpoint)
            return;

        if (!on)
        {
            if (endpoint.Active)
                Mutate(cloud => Deactivate(cloud));
        }
        else if (!endpoint.Active)
        {
            if (!(CloudTranslate || CloudClean))
                Mutate(cloud => cloud.ActiveEndpointId = id);
            else if (IsConfigured(endpoint))
                PendingChange = new PendingEndpointChange(EndpointChange.Switch, id);
            else
                CloudInvalid = true;
        }
    }

    /// <summary>Asks to delete an endpoint together with its API key; <see cref="AnswerChangeAsync"/> carries it out.</summary>
    public void DeleteEndpoint(string id)
    {
        if (Endpoints.Any(endpoint => endpoint.Id == id))
            PendingChange = new PendingEndpointChange(EndpointChange.Delete, id);
    }

    public async Task AnswerChangeAsync(bool allow)
    {
        if (PendingChange is not { } change)
            return;

        PendingChange = null;
        if (!allow || Endpoints.FirstOrDefault(endpoint => endpoint.Id == change.Id) is not { } endpoint)
            return;

        if (change.Kind == EndpointChange.Switch)
        {
            Mutate(cloud => cloud.ActiveEndpointId = change.Id);
            return;
        }

        var secretId = _settings.CloneCurrent().Cloud.Endpoints.First(item => item.Id == endpoint.Id).SecretId;
        await _secrets.DeleteSecretAsync(secretId).ConfigureAwait(true);
        _keyed.Remove(endpoint.Id);
        Mutate(cloud =>
        {
            if (cloud.ActiveEndpointId == change.Id)
                Deactivate(cloud);
            cloud.Endpoints.RemoveAll(item => item.Id == change.Id);
        });
    }

    private static void Deactivate(CloudSettings cloud)
    {
        cloud.ActiveEndpointId = "";
        cloud.Translate = false;
        cloud.Clean = false;
    }

    /// <summary>Changes the cloud settings and brings everything this page keeps of them up to date.</summary>
    private void Mutate(Action<CloudSettings> change)
    {
        var model = _settings.CloneCurrent();
        change(model.Cloud);
        _settings.Replace(model);
        CloudTranslate = model.Cloud.Translate;
        CloudClean = model.Cloud.Clean;
        CloudInvalid = false;
        Endpoints = EntriesOf(model.Cloud);
        OnPropertyChanged(nameof(ActiveEndpoint));
    }

    private IReadOnlyList<EndpointEntry> EntriesOf(CloudSettings cloud) =>
    [
        .. cloud.Endpoints.Select(endpoint => new EndpointEntry(
            endpoint.Id, endpoint.Name, endpoint.BaseUrl, endpoint.Model, _keyed.Contains(endpoint.Id), endpoint.Id == cloud.ActiveEndpointId))
    ];

    private static bool IsUsableUrl(string baseUrl)
    {
        try
        {
            CloudRoute.Create(baseUrl, "model", "key");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The key is checked for presence only; a stored key is validated when it is used.</summary>
    private static bool IsConfigured(EndpointEntry? endpoint)
    {
        if (endpoint is null)
            return false;

        try
        {
            CloudRoute.Create(endpoint.BaseUrl, endpoint.Model, endpoint.HasKey ? "key" : "");
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
        Endpoints = EntriesOf(_settings.CloneCurrent().Cloud);
        OnPropertyChanged(nameof(ActiveEndpoint));

        var saved = _settings.CloneCurrent().Composer.Microphone;
        var devices = await Task.Run(() => _microphone.InputDevices()).ConfigureAwait(true);
        _loading = true;
        MicrophoneNames = new[] { "" }.Concat(devices).ToList();
        MicrophoneIndex = Math.Max(0, MicrophoneNames.ToList().IndexOf(saved));
        _devicesLoaded = true;
        _loading = false;

        var current = ModelCatalog.All.Select(model => model.Id).ToHashSet();
        var legacy = await Task.Run(() => _models.ListInstalled().Where(model => !current.Contains(model.Id)).ToList())
            .ConfigureAwait(true);
        _legacyIds = legacy.Select(model => model.Id).ToList();
        LegacyBytes = legacy.Sum(model => model.SizeBytes);
        LegacyCount = legacy.Count;
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
