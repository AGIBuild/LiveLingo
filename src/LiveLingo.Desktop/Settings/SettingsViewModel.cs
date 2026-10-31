using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Models;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Services.Configuration;

namespace LiveLingo.Desktop.Settings;

/// <summary>Edits a copy of the settings; nothing changes until <see cref="SaveCommand"/> succeeds.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Languages = ["zh-CN", "en-US"];

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IModelManager _models;
    private readonly SettingsModel _draft;
    private IReadOnlyList<string> _legacyIds = [];

    public SettingsViewModel(ISettingsService settings, ISecretStore secrets, IModelManager models)
    {
        _settings = settings;
        _secrets = secrets;
        _models = models;
        _draft = settings.CloneCurrent();
        _summonHotkey = _draft.Hotkeys.Summon;
        _language = _draft.Appearance.Language;
        _quality = _draft.Composer.TranslatorTier == nameof(TranslatorTier.Quality);
        _glossary = _draft.Composer.Glossary;
        _cloudBaseUrl = _draft.Cloud.BaseUrl;
        _cloudModel = _draft.Cloud.Model;
        _cloudTranslate = _draft.Cloud.Translate;
        _cloudClean = _draft.Cloud.Clean;
    }

    /// <summary>Raised after a successful save or a cancel; the window should close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private string _summonHotkey;
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _quality;
    [ObservableProperty] private string _glossary;

    /// <summary>True when the hotkey text is not a valid shortcut; the window shows the localized message.</summary>
    [ObservableProperty] private bool _hotkeyInvalid;

    /// <summary>Send translation to the user's cloud model (text only; off by default).</summary>
    [ObservableProperty] private bool _cloudTranslate;

    /// <summary>Send cleanup to the user's cloud model (text only; off by default).</summary>
    [ObservableProperty] private bool _cloudClean;

    [ObservableProperty] private string _cloudBaseUrl;
    [ObservableProperty] private string _cloudModel;

    /// <summary>A new API key typed by the user; empty keeps the stored one. It is never shown again.</summary>
    [ObservableProperty] private string _cloudApiKey = "";

    /// <summary>True when a key is already in the system secret store.</summary>
    [ObservableProperty] private bool _hasStoredKey;

    /// <summary>True when a cloud feature is on but the endpoint, model or key is missing or unusable.</summary>
    [ObservableProperty] private bool _cloudInvalid;

    partial void OnSummonHotkeyChanged(string value) => HotkeyInvalid = false;

    partial void OnCloudBaseUrlChanged(string value) => CloudInvalid = false;

    partial void OnCloudModelChanged(string value) => CloudInvalid = false;

    partial void OnCloudApiKeyChanged(string value) => CloudInvalid = false;

    partial void OnCloudTranslateChanged(bool value) => CloudInvalid = false;

    partial void OnCloudCleanChanged(bool value) => CloudInvalid = false;

    /// <summary>Reads whether a key is stored and which models from earlier versions are on disk; the window calls it when it opens.</summary>
    public async Task InitializeAsync()
    {
        HasStoredKey = !string.IsNullOrEmpty(await _secrets.GetSecretAsync(CloudSettings.ApiKeySecretId).ConfigureAwait(true));

        var current = ModelCatalog.All.Select(model => model.Id).ToHashSet();
        var legacy = await Task.Run(() => _models.ListInstalled().Where(model => !current.Contains(model.Id)).ToList())
            .ConfigureAwait(true);
        _legacyIds = legacy.Select(model => model.Id).ToList();
        LegacyBytes = legacy.Sum(model => model.SizeBytes);
        LegacyCount = legacy.Count;
    }

    /// <summary>Models that earlier versions downloaded and this one no longer uses; they are only removed on request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLegacyModels))]
    private int _legacyCount;

    [ObservableProperty] private long _legacyBytes;

    public bool HasLegacyModels => LegacyCount > 0;

    [RelayCommand]
    private async Task RemoveLegacyModelsAsync()
    {
        await _models.CleanObsoleteModelsAsync(_legacyIds).ConfigureAwait(true);
        _legacyIds = [];
        LegacyCount = 0;
        LegacyBytes = 0;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            HotkeyParser.Parse("summon", SummonHotkey);
        }
        catch (ArgumentException)
        {
            HotkeyInvalid = true;
            return;
        }

        var cloudOn = CloudTranslate || CloudClean;
        if (cloudOn && !IsCloudConfigured())
        {
            CloudInvalid = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(CloudApiKey))
        {
            await _secrets.SetSecretAsync(CloudSettings.ApiKeySecretId, CloudApiKey.Trim()).ConfigureAwait(true);
            HasStoredKey = true;
            CloudApiKey = "";
        }

        _draft.Hotkeys.Summon = SummonHotkey.Trim();
        _draft.Appearance.Language = Language;
        _draft.Composer.TranslatorTier = Quality ? nameof(TranslatorTier.Quality) : nameof(TranslatorTier.Fast);
        _draft.Composer.Glossary = Glossary;
        _draft.Cloud.BaseUrl = CloudBaseUrl.Trim();
        _draft.Cloud.Model = CloudModel.Trim();
        _draft.Cloud.Translate = CloudTranslate;
        _draft.Cloud.Clean = CloudClean;
        _settings.Replace(_draft);
        CloseRequested?.Invoke();
    }

    /// <summary>Removes the stored API key and switches both cloud features off.</summary>
    [RelayCommand]
    private async Task ForgetCloudKeyAsync()
    {
        await _secrets.DeleteSecretAsync(CloudSettings.ApiKeySecretId).ConfigureAwait(true);
        HasStoredKey = false;
        CloudApiKey = "";
        CloudTranslate = false;
        CloudClean = false;
    }

    private bool IsCloudConfigured()
    {
        try
        {
            // The key itself is validated for presence only; a stored key is checked when it is used.
            CloudRoute.Create(CloudBaseUrl, CloudModel, HasStoredKey || !string.IsNullOrWhiteSpace(CloudApiKey) ? "key" : "");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
