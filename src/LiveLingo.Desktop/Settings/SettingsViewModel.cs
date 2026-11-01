using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Settings;

/// <summary>One line of the glossary list; a term whose translation equals it is kept as written.</summary>
public sealed record TermEntry(string Source, string Target);

/// <summary>The two steps that can run on the user's own cloud endpoint, each authorized on its own.</summary>
public enum CloudStep
{
    Translate,
    Clean
}

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

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IModelManager _models;
    private readonly RecentDrafts _recent;
    private readonly IMicrophone _microphone;
    private IReadOnlyList<string> _legacyIds = [];
    private bool _loading = true;
    private bool _devicesLoaded;

    public SettingsViewModel(
        ISettingsService settings, ISecretStore secrets, IModelManager models, RecentDrafts recent, IMicrophone microphone)
    {
        _settings = settings;
        _secrets = secrets;
        _models = models;
        _recent = recent;
        _microphone = microphone;

        var current = settings.CloneCurrent();
        _summonHotkey = current.Hotkeys.Summon;
        _commitKeyIndex = Math.Max(0, CommitKeys.ToList().IndexOf(current.Composer.CommitKey));
        _languageIndex = Math.Max(0, Languages.ToList().IndexOf(current.Appearance.Language));
        _themeIndex = Math.Max(0, Themes.ToList().IndexOf(current.Appearance.Theme));
        _reduceMotion = !current.Appearance.Animations;
        _targetIndex = Math.Max(0, Targets.ToList().IndexOf(current.Composer.TargetLanguage));
        _tierIndex = Math.Max(0, Tiers.ToList().IndexOf(current.Composer.TranslatorTier));
        _microphoneNames = [""];
        _microphoneIndex = 0;
        _cloudBaseUrl = current.Cloud.BaseUrl;
        _cloudModel = current.Cloud.Model;
        _cloudTranslate = current.Cloud.Translate;
        _cloudClean = current.Cloud.Clean;
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

    public ObservableCollection<TermEntry> Terms { get; }

    [ObservableProperty] private string _newTermSource = "";
    [ObservableProperty] private string _newTermTarget = "";

    // ---- privacy and cloud -----------------------------------------------------------------

    [ObservableProperty] private bool _cloudTranslate;
    [ObservableProperty] private bool _cloudClean;
    [ObservableProperty] private string _cloudBaseUrl;
    [ObservableProperty] private string _cloudModel;

    /// <summary>A new API key typed by the user; saved on request and never shown again.</summary>
    [ObservableProperty] private string _cloudApiKey = "";

    [ObservableProperty] private bool _hasStoredKey;

    /// <summary>True when a cloud feature was asked for but the endpoint, model or key is missing or unusable.</summary>
    [ObservableProperty] private bool _cloudInvalid;

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
    partial void OnTierIndexChanged(int value) => ApplyIfSelected(value);

    /// <summary>A combo box reports -1 while its items are being replaced; that is not a choice.</summary>
    private void ApplyIfSelected(int index)
    {
        if (index >= 0)
            Apply();
    }

    partial void OnCloudBaseUrlChanged(string value)
    {
        CloudInvalid = false;
        Apply();
    }

    partial void OnCloudModelChanged(string value)
    {
        CloudInvalid = false;
        Apply();
    }

    partial void OnCloudApiKeyChanged(string value) => CloudInvalid = false;

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
        model.Composer.TranslatorTier = Tiers[TierIndex];
        if (_devicesLoaded)
            model.Composer.Microphone = MicrophoneIndex < MicrophoneNames.Count ? MicrophoneNames[MicrophoneIndex] : "";
        model.Composer.Glossary = SerializeTerms(Terms);

        if (!(CloudTranslate || CloudClean) || IsCloudConfigured())
        {
            model.Cloud.BaseUrl = CloudBaseUrl.Trim();
            model.Cloud.Model = CloudModel.Trim();
        }
        else
        {
            CloudInvalid = true;
        }

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

    /// <summary>Turning a step on needs the user's consent every time (<see cref="AnswerConsent"/>); turning it off is immediate.</summary>
    public void SetCloud(CloudStep step, bool on)
    {
        var translation = step == CloudStep.Translate;
        if (!on)
            DisableCloud(translation);
        else if (!(translation ? CloudTranslate : CloudClean))
            PendingConsent = step;
    }

    public void AnswerConsent(bool allow)
    {
        if (PendingConsent is not { } step)
            return;

        PendingConsent = null;
        if (allow)
            TryEnableCloud(step == CloudStep.Translate);
    }

    /// <summary>Turns a cloud step on after the user consented; refused while the endpoint, model or key is not set.</summary>
    public bool TryEnableCloud(bool translation)
    {
        if (!IsCloudConfigured())
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

    /// <summary>Saves the typed key into the system secret store and forgets it here.</summary>
    [RelayCommand]
    private async Task SaveCloudKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(CloudApiKey))
            return;

        await _secrets.SetSecretAsync(CloudSettings.ApiKeySecretId, CloudApiKey.Trim()).ConfigureAwait(true);
        HasStoredKey = true;
        CloudApiKey = "";
    }

    /// <summary>Removes the stored API key and switches both cloud steps off.</summary>
    [RelayCommand]
    private async Task ForgetCloudKeyAsync()
    {
        await _secrets.DeleteSecretAsync(CloudSettings.ApiKeySecretId).ConfigureAwait(true);
        HasStoredKey = false;
        CloudApiKey = "";
        CloudTranslate = false;
        CloudClean = false;
        Apply();
    }

    private bool IsCloudConfigured()
    {
        try
        {
            // The key is checked for presence only; a stored key is validated when it is used.
            CloudRoute.Create(CloudBaseUrl, CloudModel, HasStoredKey || !string.IsNullOrWhiteSpace(CloudApiKey) ? "key" : "");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ---- recent drafts and models -----------------------------------------------------------------

    [RelayCommand]
    private void ClearRecent() => _recent.Clear();

    /// <summary>Reads what the system provides (key, devices, models on disk); the window calls it when it opens.</summary>
    public async Task InitializeAsync()
    {
        HasStoredKey = !string.IsNullOrEmpty(await _secrets.GetSecretAsync(CloudSettings.ApiKeySecretId).ConfigureAwait(true));

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
