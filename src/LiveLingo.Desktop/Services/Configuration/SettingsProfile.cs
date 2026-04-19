using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveLingo.Core;
using LiveLingo.Core.Speech;

namespace LiveLingo.Desktop.Services.Configuration;

public partial class SettingsModel : ObservableObject
{
    public const int CurrentSchemaVersion = 2;

    [ObservableProperty] private int _schemaVersion = CurrentSchemaVersion;
    [ObservableProperty] private HotkeySettings _hotkeys = new();
    [ObservableProperty] private TranslationSettings _translation = new();
    [ObservableProperty] private ProcessingSettings _processing = new();
    [ObservableProperty] private SpeechSettings _speech = new();
    [ObservableProperty] private UISettings _uI = new();
    [ObservableProperty] private UpdateSettings _update = new();
    [ObservableProperty] private AdvancedSettings _advanced = new();

    public static SettingsModel CreateDefault() => new();

    public SettingsModel DeepClone()
    {
        return new SettingsModel
        {
            SchemaVersion = SchemaVersion,
            Hotkeys = Hotkeys.DeepClone(),
            Translation = Translation.DeepClone(),
            Processing = Processing.DeepClone(),
            Speech = Speech.DeepClone(),
            UI = UI.DeepClone(),
            Update = Update.DeepClone(),
            Advanced = Advanced.DeepClone()
        };
    }
}

/// <summary>
/// Persisted speech-recognition preferences. The <see cref="ISpeechEngineSelector"/> turns the
/// stored <see cref="RoutingMode"/> string into an <see cref="SttRoutingMode"/> at runtime, so adding
/// a new mode does not require a settings migration.
/// </summary>
public partial class SpeechSettings : ObservableObject
{
    [ObservableProperty] private string _routingMode = nameof(SttRoutingMode.AccuracyFirst);

    /// <summary>
    /// Optional override for the speech-to-text model id. When null the selector picks the best model
    /// for the current routing mode (Cohere Transcribe today). Setting this lets advanced users pin a
    /// specific bundle without changing the routing mode.
    /// </summary>
    [ObservableProperty] private string? _activeModelId;

    public SpeechSettings DeepClone() => new()
    {
        RoutingMode = RoutingMode,
        ActiveModelId = ActiveModelId
    };
}

public partial class HotkeySettings : ObservableObject
{
    [ObservableProperty] private string _overlayToggle = "Ctrl+Alt+T";

    public HotkeySettings DeepClone() => new() { OverlayToggle = OverlayToggle };
}

public partial class TranslationSettings : ObservableObject
{
    [ObservableProperty] private string _defaultSourceLanguage = "zh";
    [ObservableProperty] private string _defaultTargetLanguage = "en";
    [ObservableProperty] private string? _activeTranslationModelId;
    [ObservableProperty] private List<LanguagePair> _languagePairs = [new("zh", "en")];
    [ObservableProperty] private ModelPolicySettings _modelPolicy = new();
    [ObservableProperty] private CloudProviderSettings _cloudProvider = new();
    [ObservableProperty] private OllamaProviderSettings _ollamaProvider = new();
    [ObservableProperty] private List<GlossaryEntrySettings> _glossary = [];

    public TranslationSettings DeepClone()
    {
        return new TranslationSettings
        {
            DefaultSourceLanguage = DefaultSourceLanguage,
            DefaultTargetLanguage = DefaultTargetLanguage,
            ActiveTranslationModelId = ActiveTranslationModelId,
            LanguagePairs = LanguagePairs.Select(pair => pair.DeepClone()).ToList(),
            ModelPolicy = ModelPolicy.DeepClone(),
            CloudProvider = CloudProvider.DeepClone(),
            OllamaProvider = OllamaProvider.DeepClone(),
            Glossary = Glossary.Select(e => e.DeepClone()).ToList()
        };
    }
}

/// <summary>
/// Serialized representation of a single glossary term mapping.
/// Use <see cref="CoreOptionsSync"/> to convert to <see cref="LiveLingo.Core.Translation.GlossaryEntry"/>.
/// </summary>
public sealed class GlossaryEntrySettings
{
    public string SourceTerm { get; set; } = string.Empty;
    public string TargetTerm { get; set; } = string.Empty;

    /// <summary>BCP-47 source language code constraint; null means applies to all source languages.</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>BCP-47 target language code constraint; null means applies to all target languages.</summary>
    public string? TargetLanguage { get; set; }

    public GlossaryEntrySettings DeepClone() => new()
    {
        SourceTerm = SourceTerm,
        TargetTerm = TargetTerm,
        SourceLanguage = SourceLanguage,
        TargetLanguage = TargetLanguage
    };
}

public partial class ModelPolicySettings : ObservableObject
{
    [ObservableProperty] private string _routingMode = nameof(TranslationRoutingMode.PreferLocal);
    [ObservableProperty] private bool _routeUnsupportedPairsToCloud = true;
    [ObservableProperty] private bool _routePostProcessingToCloud;
    [ObservableProperty] private string? _preferredLocalTranslationModelId;

    public ModelPolicySettings DeepClone() => new()
    {
        RoutingMode = RoutingMode,
        RouteUnsupportedPairsToCloud = RouteUnsupportedPairsToCloud,
        RoutePostProcessingToCloud = RoutePostProcessingToCloud,
        PreferredLocalTranslationModelId = PreferredLocalTranslationModelId
    };
}

/// <summary>
/// User-supplied configuration for the Ollama local daemon. Ollama itself is installed
/// and run by the user (e.g. <c>ollama serve</c>); we only point at a running instance.
/// </summary>
public partial class OllamaProviderSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _baseUrl = "http://localhost:11434";
    [ObservableProperty] private string? _translationModelId;
    [ObservableProperty] private string? _postProcessingModelId;

    public OllamaProviderSettings DeepClone() => new()
    {
        Enabled = Enabled,
        BaseUrl = BaseUrl,
        TranslationModelId = TranslationModelId,
        PostProcessingModelId = PostProcessingModelId
    };
}

public partial class CloudProviderSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _presetId = CloudProviderPresetCatalog.OpenAI.Id;
    [ObservableProperty] private string _providerType = "OpenAICompatible";
    [ObservableProperty] private string _baseUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string? _apiKey;
    [ObservableProperty] private string? _apiKeySecretId;
    [ObservableProperty] private string? _translationModelId;
    [ObservableProperty] private string? _postProcessingModelId;

    public CloudProviderSettings DeepClone() => new()
    {
        Enabled = Enabled,
        PresetId = PresetId,
        ProviderType = ProviderType,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        ApiKeySecretId = ApiKeySecretId,
        TranslationModelId = TranslationModelId,
        PostProcessingModelId = PostProcessingModelId
    };
}

public partial class LanguagePair : ObservableObject
{
    public LanguagePair()
    {
    }

    public LanguagePair(string source, string target)
    {
        _source = source;
        _target = target;
    }

    [ObservableProperty] private string _source = "zh";
    [ObservableProperty] private string _target = "en";

    public LanguagePair DeepClone() => new(Source, Target);
}

public partial class ProcessingSettings : ObservableObject
{
    [ObservableProperty] private string _defaultMode = "Off";

    public ProcessingSettings DeepClone() => new() { DefaultMode = DefaultMode };
}

public partial class UISettings : ObservableObject
{
    [ObservableProperty] private double _overlayOpacity = 0.95;
    [ObservableProperty] private string _defaultInjectionMode = "PasteAndSend";
    [ObservableProperty] private string _language = "en-US";
    [ObservableProperty] private OverlayPosition? _lastOverlayPosition;

    public UISettings DeepClone()
    {
        return new UISettings
        {
            OverlayOpacity = OverlayOpacity,
            DefaultInjectionMode = DefaultInjectionMode,
            Language = Language,
            LastOverlayPosition = LastOverlayPosition?.DeepClone()
        };
    }
}

public partial class OverlayPosition : ObservableObject
{
    public OverlayPosition()
    {
    }

    public OverlayPosition(int x, int y)
    {
        _x = x;
        _y = y;
    }

    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;

    public OverlayPosition DeepClone() => new(X, Y);
}

public partial class UpdateSettings : ObservableObject
{
    [ObservableProperty] private string _updateUrl = string.Empty;
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
    [ObservableProperty] private string? _huggingFaceToken;
    [ObservableProperty] private string? _huggingFaceTokenSecretId;

    public AdvancedSettings DeepClone() => new()
    {
        ModelStoragePath = ModelStoragePath,
        InferenceThreads = InferenceThreads,
        LogLevel = LogLevel,
        HuggingFaceMirror = HuggingFaceMirror,
        HuggingFaceToken = HuggingFaceToken,
        HuggingFaceTokenSecretId = HuggingFaceTokenSecretId
    };
}
