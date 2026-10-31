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
/// anywhere until the user switches a feature on. Only text is sent, never audio. The API key is not stored here;
/// it lives in the system secret store under <see cref="ApiKeySecretId"/>.
/// </summary>
public partial class CloudSettings : ObservableObject
{
    public const string ApiKeySecretId = "cloud-api-key";

    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private bool _translate;
    [ObservableProperty] private bool _clean;

    public CloudSettings DeepClone() => new() { BaseUrl = BaseUrl, Model = Model, Translate = Translate, Clean = Clean };
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

    public ComposerSettings DeepClone() => new() { TranslatorTier = TranslatorTier, Glossary = Glossary };
}

public partial class AppearanceSettings : ObservableObject
{
    [ObservableProperty] private string _language = "en-US";
    [ObservableProperty] private bool _animations = true;

    public AppearanceSettings DeepClone() => new() { Language = Language, Animations = Animations };
}

public partial class UpdateSettings : ObservableObject
{
    public const string DefaultUpdateUrl = "https://github.com/user/LiveLingo";

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
