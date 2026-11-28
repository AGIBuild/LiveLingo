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
/// anywhere until the user explicitly selects and authorizes a cloud endpoint for cleanup or translation. Only text is
/// sent, never audio. Cleanup and translation may independently use local models or any saved cloud endpoint.
/// </summary>
public partial class CloudSettings : ObservableObject
{
    /// <summary>The secret id the single endpoint of earlier versions kept its API key under; its migrated endpoint keeps using it.</summary>
    public const string LegacyApiKeySecretId = "cloud-api-key";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranslatorEndpoint), nameof(CleanerEndpoint), nameof(SpeechEndpoint))]
    private List<CloudEndpoint> _endpoints = [];

    /// <summary>The endpoint ID selected for speech recognition, or empty when speech runs on the local model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Speech), nameof(SpeechEndpoint))]
    private string _speechEndpointId = "";

    /// <summary>The endpoint ID selected for translation, or empty when translation runs on the local model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Translate), nameof(TranslatorEndpoint))]
    private string _translatorEndpointId = "";

    /// <summary>The endpoint ID selected for cleanup and whole-text polish, or empty when cleanup runs on the local model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Clean), nameof(CleanerEndpoint))]
    private string _cleanerEndpointId = "";

    /// <summary>True when speech recognition is routed to a cloud endpoint.</summary>
    [JsonIgnore]
    public bool Speech => !string.IsNullOrEmpty(SpeechEndpointId);

    /// <summary>True when translation is routed to a cloud endpoint.</summary>
    [JsonIgnore]
    public bool Translate => !string.IsNullOrEmpty(TranslatorEndpointId);

    /// <summary>True when cleanup and polish are routed to a cloud endpoint.</summary>
    [JsonIgnore]
    public bool Clean => !string.IsNullOrEmpty(CleanerEndpointId);

    /// <summary>The cloud endpoint currently assigned to speech recognition, or null when speech is local.</summary>
    [JsonIgnore]
    public CloudEndpoint? SpeechEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == SpeechEndpointId);

    /// <summary>The cloud endpoint currently assigned to translation, or null when translation is local.</summary>
    [JsonIgnore]
    public CloudEndpoint? TranslatorEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == TranslatorEndpointId);

    /// <summary>The cloud endpoint currently assigned to cleanup, or null when cleanup is local.</summary>
    [JsonIgnore]
    public CloudEndpoint? CleanerEndpoint => Endpoints.FirstOrDefault(endpoint => endpoint.Id == CleanerEndpointId);

    /// <summary>Legacy fields from earlier schema-3 revisions; read only to migrate seamlessly, never written.</summary>
    [JsonPropertyName("baseUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyBaseUrl { get; set; }

    [JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyModel { get; set; }

    [JsonPropertyName("activeEndpointId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyActiveEndpointId { get; set; }

    [JsonPropertyName("translate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyTranslate { get; set; }

    [JsonPropertyName("clean"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyClean { get; set; }

    /// <summary>
    /// Migrates both the earliest single-endpoint fields (<c>baseUrl</c>/<c>model</c>) and the shared-active-endpoint
    /// fields (<c>activeEndpointId</c> + boolean <c>translate</c>/<c>clean</c>) into explicit per-stage endpoint bindings
    /// without altering where any step currently sends text.
    /// </summary>
    public bool MigrateLegacy()
    {
        if (LegacyBaseUrl is null && LegacyModel is null && LegacyActiveEndpointId is null
            && LegacyTranslate is null && LegacyClean is null)
        {
            return false;
        }

        var activeId = (LegacyActiveEndpointId ?? "").Trim();

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
            if (string.IsNullOrEmpty(activeId))
                activeId = endpoint.Id;
        }

        if (string.IsNullOrEmpty(TranslatorEndpointId) && LegacyTranslate == true
            && !string.IsNullOrEmpty(activeId) && Endpoints.Any(endpoint => endpoint.Id == activeId))
        {
            TranslatorEndpointId = activeId;
        }

        if (string.IsNullOrEmpty(CleanerEndpointId) && LegacyClean == true
            && !string.IsNullOrEmpty(activeId) && Endpoints.Any(endpoint => endpoint.Id == activeId))
        {
            CleanerEndpointId = activeId;
        }

        LegacyBaseUrl = null;
        LegacyModel = null;
        LegacyActiveEndpointId = null;
        LegacyTranslate = null;
        LegacyClean = null;
        return true;
    }

    public CloudSettings DeepClone() => new()
    {
        Endpoints = [.. Endpoints.Select(endpoint => endpoint.DeepClone())],
        SpeechEndpointId = SpeechEndpointId,
        TranslatorEndpointId = TranslatorEndpointId,
        CleanerEndpointId = CleanerEndpointId,
        LegacyBaseUrl = LegacyBaseUrl,
        LegacyModel = LegacyModel,
        LegacyActiveEndpointId = LegacyActiveEndpointId,
        LegacyTranslate = LegacyTranslate,
        LegacyClean = LegacyClean
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

/// <summary>One built-in override or user-created custom polish preset stored in <c>settings.json</c>.</summary>
public sealed class PolishPresetEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Gate { get; set; } = nameof(LiveLingo.Core.Cleanup.PolishGateKind.FactAnchor);
    public bool Enabled { get; set; } = true;
    public string Instructions { get; set; } = "";

    public PolishPresetEntry DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Gate = Gate,
        Enabled = Enabled,
        Instructions = Instructions
    };
}

public partial class ComposerSettings : ObservableObject
{
    /// <summary>Selected speech second-pass model ID (<see cref="LiveLingo.Core.Models.ModelCatalog.SpeechModels"/>), or empty to use hardware Best Match.</summary>
    [ObservableProperty] private string _speechModel = "";

    /// <summary>Selected cleanup/polish model ID (<see cref="LiveLingo.Core.Models.ModelCatalog.CleanerModels"/>), or empty to use hardware Best Match.</summary>
    [ObservableProperty] private string _cleanerModel = "";

    /// <summary>Selected translation model ID (<see cref="LiveLingo.Core.Models.ModelCatalog.TranslatorModels"/>), or empty to use hardware Best Match.</summary>
    [ObservableProperty] private string _translatorModel = "";

    /// <summary>Earlier schema-3 translation tier ("Fast" / "Quality"); read only to migrate existing files without forcing unexpected downloads.</summary>
    [JsonPropertyName("translatorTier"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTranslatorTier { get; set; }

    /// <summary>The user's terms, one per line (see <see cref="LiveLingo.Core.Translation.Glossary"/>).</summary>
    [ObservableProperty] private string _glossary = "";

    /// <summary>
    /// Whether LiveLingo's built-in bilingual technical vocabulary (Agent, Skill, Prompt, API, VS Code…) is merged
    /// with the user's glossary for speech hotwords, cleanup and the fidelity gate.
    /// </summary>
    [ObservableProperty] private bool _builtInGlossary = true;

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
    /// The user's polish requirements (see <c>PolishStyle</c>); empty means the built-in ones. Kept for seamless
    /// legacy migration and single-prompt compatibility alongside <see cref="PolishPresets"/>.
    /// </summary>
    [ObservableProperty] private string _polishPrompt = "";

    /// <summary>Default scenario polish mode ID (<see cref="LiveLingo.Core.Cleanup.PolishPresets"/>) when no app choice applies.</summary>
    [ObservableProperty] private string _defaultPolishMode = LiveLingo.Core.Cleanup.PolishPresets.NaturalId;

    /// <summary>Whether known target host apps (IDEs/terminals, issue trackers, docs, mail) start on their matching polish preset.</summary>
    [ObservableProperty] private bool _smartHostPolish = true;

    /// <summary>Built-in preset overrides (visibility or tuned instructions) and user-created custom polish presets.</summary>
    [ObservableProperty] private List<PolishPresetEntry> _polishPresets = [];

    public IReadOnlyList<LiveLingo.Core.Cleanup.PolishPreset> EffectivePolishPresets()
    {
        var defaultId = string.IsNullOrWhiteSpace(DefaultPolishMode)
            ? LiveLingo.Core.Cleanup.PolishPresets.NaturalId
            : DefaultPolishMode;
        var result = new List<LiveLingo.Core.Cleanup.PolishPreset>();

        foreach (var builtIn in LiveLingo.Core.Cleanup.PolishPresets.BuiltIns)
        {
            var entry = PolishPresets.FirstOrDefault(e => string.Equals(e.Id, builtIn.Id, StringComparison.Ordinal));
            var customText = entry?.Instructions ?? "";
            if (customText.Length == 0 && builtIn.Id == LiveLingo.Core.Cleanup.PolishPresets.NaturalId
                && LiveLingo.Core.Cleanup.PolishStyle.IsUsable(PolishPrompt)
                && LiveLingo.Core.Cleanup.PolishStyle.ToStored(PolishPrompt) is { Length: > 0 } legacyPrompt)
            {
                customText = legacyPrompt;
            }
            var enabled = (entry?.Enabled ?? true) || string.Equals(builtIn.Id, defaultId, StringComparison.Ordinal);
            result.Add(builtIn with { Enabled = enabled, CustomInstructions = customText });
        }

        foreach (var entry in PolishPresets)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || LiveLingo.Core.Cleanup.PolishPresets.IsBuiltInId(entry.Id))
                continue;
            if (!LiveLingo.Core.Cleanup.PolishPresets.IsValidName(entry.Name)
                || !LiveLingo.Core.Cleanup.PolishStyle.IsUsable(entry.Instructions))
                continue;

            var gate = Enum.TryParse<LiveLingo.Core.Cleanup.PolishGateKind>(entry.Gate, ignoreCase: true, out var parsed)
                ? parsed
                : LiveLingo.Core.Cleanup.PolishGateKind.FactAnchor;
            var route = gate == LiveLingo.Core.Cleanup.PolishGateKind.FactAnchor
                ? LiveLingo.Core.Cleanup.PolishRouteKind.Holistic
                : LiveLingo.Core.Cleanup.PolishRouteKind.Piecewise;
            var enabled = entry.Enabled || string.Equals(entry.Id, defaultId, StringComparison.Ordinal);
            result.Add(new LiveLingo.Core.Cleanup.PolishPreset(
                entry.Id,
                entry.Name.Trim(),
                LiveLingo.Core.Cleanup.PolishPresets.IsValidDescription(entry.Description) ? entry.Description.Trim() : "",
                gate,
                route,
                BuiltIn: false,
                Enabled: enabled,
                DefaultInstructions: entry.Instructions,
                CustomInstructions: entry.Instructions));
        }

        if (!result.Any(p => string.Equals(p.Id, defaultId, StringComparison.Ordinal) && p.Enabled))
        {
            var idx = result.FindIndex(p => p.Id == LiveLingo.Core.Cleanup.PolishPresets.NaturalId);
            if (idx >= 0)
                result[idx] = result[idx] with { Enabled = true };
        }

        return result;
    }

    public string ResolvedDefaultPolishMode()
    {
        var all = EffectivePolishPresets();
        return all.FirstOrDefault(p => string.Equals(p.Id, DefaultPolishMode, StringComparison.Ordinal) && p.Enabled)?.Id
            ?? LiveLingo.Core.Cleanup.PolishPresets.NaturalId;
    }

    public LiveLingo.Core.Translation.Glossary EffectiveGlossary() =>
        LiveLingo.Core.Translation.Glossary.Parse(Glossary, BuiltInGlossary);

    public LiveLingo.Core.Models.ModelSelection ResolveSelection(
        LiveLingo.Core.Models.HardwareProfile hardware,
        LiveLingo.Core.Models.IModelManager? installedModels = null)
    {
        var resolved = LiveLingo.Core.Models.ModelAdvisor.Resolve(SpeechModel, CleanerModel, TranslatorModel, hardware);
        if (installedModels is null)
            return resolved;

        return new LiveLingo.Core.Models.ModelSelection(
            PreferInstalledWhenUnset(LiveLingo.Core.Inference.ModelStage.Speech, SpeechModel, resolved.SpeechModelId, hardware, installedModels),
            PreferInstalledWhenUnset(LiveLingo.Core.Inference.ModelStage.Cleaner, CleanerModel, resolved.CleanerModelId, hardware, installedModels),
            PreferInstalledWhenUnset(LiveLingo.Core.Inference.ModelStage.Translator, TranslatorModel, resolved.TranslatorModelId, hardware, installedModels));
    }

    private static string PreferInstalledWhenUnset(
        LiveLingo.Core.Inference.ModelStage stage,
        string? explicitId,
        string recommendedId,
        LiveLingo.Core.Models.HardwareProfile hardware,
        LiveLingo.Core.Models.IModelManager models)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
            return recommendedId;

        var recommended = LiveLingo.Core.Models.ModelCatalog.FindOption(stage, recommendedId)!;
        if (models.HasAllExpectedLocalAssets(recommended))
            return recommendedId;

        foreach (var candidate in LiveLingo.Core.Models.ModelCatalog.OptionsFor(stage))
        {
            if (models.HasAllExpectedLocalAssets(candidate)
                && LiveLingo.Core.Models.ModelAdvisor.Evaluate(candidate, hardware).IsSelectable)
            {
                return candidate.Id;
            }
        }

        return recommendedId;
    }

    /// <summary>
    /// Converts the earlier single <c>translatorTier</c> field into explicit stage model IDs matching what that version ran,
    /// and migrates a custom single <c>polishPrompt</c> onto the built-in <c>natural</c> preset override.
    /// </summary>
    public bool MigrateLegacy()
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(PolishPrompt)
            && LiveLingo.Core.Cleanup.PolishStyle.ToStored(PolishPrompt) is { Length: > 0 } customNatural
            && !PolishPresets.Any(e => e.Id == LiveLingo.Core.Cleanup.PolishPresets.NaturalId))
        {
            PolishPresets.Add(new PolishPresetEntry
            {
                Id = LiveLingo.Core.Cleanup.PolishPresets.NaturalId,
                Enabled = true,
                Gate = nameof(LiveLingo.Core.Cleanup.PolishGateKind.Verbatim),
                Instructions = customNatural
            });
            changed = true;
        }

        if (LegacyTranslatorTier is null)
            return changed;

        if (string.IsNullOrWhiteSpace(SpeechModel))
            SpeechModel = LiveLingo.Core.Models.ModelCatalog.SpeechSecondPass.Id;
        if (string.IsNullOrWhiteSpace(CleanerModel))
            CleanerModel = LiveLingo.Core.Models.ModelCatalog.Cleaner.Id;
        if (string.IsNullOrWhiteSpace(TranslatorModel))
        {
            var quality = string.Equals(
                LegacyTranslatorTier.Trim(),
                nameof(LiveLingo.Core.Models.TranslatorTier.Quality),
                StringComparison.OrdinalIgnoreCase);
            TranslatorModel = quality
                ? LiveLingo.Core.Models.ModelCatalog.TranslatorQuality.Id
                : LiveLingo.Core.Models.ModelCatalog.TranslatorFast.Id;
        }

        LegacyTranslatorTier = null;
        return true;
    }

    public ComposerSettings DeepClone() => new()
    {
        CleanupEnabled = CleanupEnabled,
        DefaultOutput = DefaultOutput,
        AutoDictate = AutoDictate,
        PreloadSpeech = PreloadSpeech,
        RememberChoices = RememberChoices,
        PolishPrompt = PolishPrompt,
        DefaultPolishMode = DefaultPolishMode,
        SmartHostPolish = SmartHostPolish,
        PolishPresets = [.. PolishPresets.Select(entry => entry.DeepClone())],
        SpeechModel = SpeechModel,
        CleanerModel = CleanerModel,
        TranslatorModel = TranslatorModel,
        LegacyTranslatorTier = LegacyTranslatorTier,
        Glossary = Glossary,
        BuiltInGlossary = BuiltInGlossary,
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
