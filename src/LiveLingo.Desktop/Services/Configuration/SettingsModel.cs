using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveLingo.Desktop.Services.Configuration;

public partial class SettingsModel : ObservableObject
{
    public const int CurrentSchemaVersion = 3;

    [ObservableProperty] private int _schemaVersion = CurrentSchemaVersion;
    [ObservableProperty] private HotkeySettings _hotkeys = new();
    [ObservableProperty] private AppearanceSettings _appearance = new();
    [ObservableProperty] private ComposerSettings _composer = new();
    [ObservableProperty] private CloudSettings _cloud = new();
    [ObservableProperty] private UpdateSettings _update = new();
    [ObservableProperty] private AdvancedSettings _advanced = new();

    public static SettingsModel CreateDefault() => new();

    public SettingsModel DeepClone() => new()
    {
        SchemaVersion = SchemaVersion,
        Hotkeys = Hotkeys.DeepClone(),
        Appearance = Appearance.DeepClone(),
        Composer = Composer.DeepClone(),
        Cloud = Cloud.DeepClone(),
        Update = Update.DeepClone(),
        Advanced = Advanced.DeepClone()
    };
}

/// <summary>
/// Optional cloud models (Microsoft Foundry or any OpenAI-compatible service). Off by default: nothing is sent
/// anywhere until the user switches a feature on. Only text is sent, never audio. The user may keep any number of
/// endpoints, at most one of them enabled (<see cref="ActiveEndpointId"/>); the steps that are switched on use that one.
/// </summary>
public partial class CloudSettings : ObservableObject
{
    /// <summary>The secret id the single endpoint of earlier versions kept its API key under; its migrated endpoint keeps using it.</summary>
    public const string LegacyApiKeySecretId = "cloud-api-key";

    [ObservableProperty] private List<CloudEndpoint> _endpoints = [];

    /// <summary>The id of the enabled endpoint; empty when none is enabled, which keeps everything local.</summary>
    [ObservableProperty] private string _activeEndpointId = "";

    [ObservableProperty] private bool _translate;
    [ObservableProperty] private bool _clean;

    /// <summary>The address of the one endpoint earlier versions had. Read only to migrate it, never written (see <see cref="MigrateLegacy"/>).</summary>
    [JsonPropertyName("baseUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyBaseUrl { get; set; }

    [JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyModel { get; set; }

    /// <summary>The enabled endpoint, or null when none is.</summary>
    [JsonIgnore]
    public CloudEndpoint? Active => Endpoints.FirstOrDefault(endpoint => endpoint.Id == ActiveEndpointId);

    /// <summary>
    /// Turns the single address and model of earlier versions into an endpoint named "Default" that keeps its API key.
    /// It is enabled only if a cloud step was on, so a migration never changes where text goes. Returns true when
    /// something was converted and the settings must be saved.
    /// </summary>
    public bool MigrateLegacy()
    {
        if (LegacyBaseUrl is null && LegacyModel is null)
            return false;

        if (Endpoints.Count == 0 && !string.IsNullOrWhiteSpace(LegacyBaseUrl))
        {
            var endpoint = new CloudEndpoint
            {
                Id = CloudEndpoint.NewId(),
                Name = "Default",
                BaseUrl = LegacyBaseUrl.Trim(),
                Model = (LegacyModel ?? "").Trim(),
                SecretId = LegacyApiKeySecretId
            };
            Endpoints = [endpoint];
            ActiveEndpointId = Translate || Clean ? endpoint.Id : "";
        }

        if (Active is null)
        {
            Translate = false;
            Clean = false;
        }

        LegacyBaseUrl = null;
        LegacyModel = null;
        return true;
    }

    public CloudSettings DeepClone() => new()
    {
        Endpoints = [.. Endpoints.Select(endpoint => endpoint.DeepClone())],
        ActiveEndpointId = ActiveEndpointId,
        Translate = Translate,
        Clean = Clean,
        LegacyBaseUrl = LegacyBaseUrl,
        LegacyModel = LegacyModel
    };
}

/// <summary>One OpenAI-compatible endpoint the user saved. The API key is not stored here; it lives in the system secret store under <see cref="SecretId"/>.</summary>
public sealed class CloudEndpoint
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string SecretId { get; set; } = "";

    public static string NewId() => Guid.NewGuid().ToString("N");

    public CloudEndpoint DeepClone() => new() { Id = Id, Name = Name, BaseUrl = BaseUrl, Model = Model, SecretId = SecretId };
}

public partial class HotkeySettings : ObservableObject
{
    [ObservableProperty] private string _summon = "Ctrl+Alt+T";

    public HotkeySettings DeepClone() => new() { Summon = Summon };
}

public partial class ComposerSettings : ObservableObject
{
    /// <summary>Name of a <see cref="LiveLingo.Core.Models.TranslatorTier"/>; the fast tier suits 16 GB machines.</summary>
    [ObservableProperty] private string _translatorTier = nameof(LiveLingo.Core.Models.TranslatorTier.Fast);

    /// <summary>The user's terms, one per line (see <see cref="LiveLingo.Core.Translation.Glossary"/>).</summary>
    [ObservableProperty] private string _glossary = "";

    /// <summary>"Enter" or "CtrlEnter": the key that writes the result; Shift+Enter always inserts a line break.</summary>
    [ObservableProperty] private string _commitKey = "Enter";

    /// <summary>Name of a <see cref="LiveLingo.Core.Translation.TargetLanguage"/>: what "Translate" writes.</summary>
    [ObservableProperty] private string _targetLanguage = "English";

    /// <summary>Name of the input device used for dictation; empty means the system default.</summary>
    [ObservableProperty] private string _microphone = "";

    /// <summary>False turns cleanup off: no sentence is cleaned, the original wording is offered instead.</summary>
    [ObservableProperty] private bool _cleanupEnabled = true;

    /// <summary>
    /// Name of a <see cref="LiveLingo.Desktop.Composer.ComposerOutput"/>: what a composer starts with in an application
    /// that has no remembered choice. Cleanup unless the user says otherwise.
    /// </summary>
    [ObservableProperty] private string _defaultOutput = nameof(LiveLingo.Desktop.Composer.ComposerOutput.Clean);

    /// <summary>The microphone opens as soon as the composer appears (off: the user starts dictation).</summary>
    [ObservableProperty] private bool _autoDictate;

    /// <summary>
    /// The speech models are loaded in the background when the application starts, so the first dictation does not wait
    /// for them (about 1.5 GB of memory while they are loaded). Off: they are loaded when the composer opens.
    /// </summary>
    [ObservableProperty] private bool _preloadSpeech = true;

    /// <summary>
    /// The output and the translation direction last chosen for an application are used the next time the composer
    /// opens for it (see <c>ComposerPreferences</c>). On by default; nothing but the application's identity is kept.
    /// </summary>
    [ObservableProperty] private bool _rememberChoices = true;

    /// <summary>
    /// The user's polish requirements (see <c>PolishStyle</c>); empty means the built-in ones. A text that is not usable
    /// (damaged file, too long) is treated as empty wherever it is used.
    /// </summary>
    [ObservableProperty] private string _polishPrompt = "";

    public ComposerSettings DeepClone() => new()
    {
        CleanupEnabled = CleanupEnabled,
        DefaultOutput = DefaultOutput,
        AutoDictate = AutoDictate,
        PreloadSpeech = PreloadSpeech,
        RememberChoices = RememberChoices,
        PolishPrompt = PolishPrompt,
        TranslatorTier = TranslatorTier,
        Glossary = Glossary,
        CommitKey = CommitKey,
        TargetLanguage = TargetLanguage,
        Microphone = Microphone
    };
}

public partial class AppearanceSettings : ObservableObject
{
    [ObservableProperty] private string _language = "en-US";
    [ObservableProperty] private bool _animations = true;

    /// <summary>"Auto" follows the system; "Light" or "Dark" forces a theme.</summary>
    [ObservableProperty] private string _theme = "Auto";

    public AppearanceSettings DeepClone() => new() { Language = Language, Animations = Animations, Theme = Theme };
}

public partial class UpdateSettings : ObservableObject
{
    public const string DefaultUpdateUrl = "https://github.com/AGIBuild/LiveLingo";

    /// <summary>The default of 1.x and 2.0.0, which pointed at no repository; it was saved into every settings file.</summary>
    public const string PlaceholderUpdateUrl = "https://github.com/user/LiveLingo";

    /// <summary>Where updates are looked for; empty turns updates off.</summary>
    [ObservableProperty] private string _updateUrl = DefaultUpdateUrl;
    [ObservableProperty] private int _checkIntervalHours = 4;

    public UpdateSettings DeepClone() => new()
    {
        UpdateUrl = UpdateUrl,
        CheckIntervalHours = CheckIntervalHours
    };
}

public partial class AdvancedSettings : ObservableObject
{
    [ObservableProperty] private string? _modelStoragePath;
    [ObservableProperty] private int _inferenceThreads;
    [ObservableProperty] private string _logLevel = "Information";
    [ObservableProperty] private string? _huggingFaceMirror;

    public AdvancedSettings DeepClone() => new()
    {
        ModelStoragePath = ModelStoragePath,
        InferenceThreads = InferenceThreads,
        LogLevel = LogLevel,
        HuggingFaceMirror = HuggingFaceMirror
    };
}
