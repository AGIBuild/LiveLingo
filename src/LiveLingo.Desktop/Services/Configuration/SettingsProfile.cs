using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiveLingo.Desktop.Services.Configuration;

public partial class SettingsModel : ObservableObject
{
    public const int CurrentSchemaVersion = 2;

    [ObservableProperty] private int _schemaVersion = CurrentSchemaVersion;
    [ObservableProperty] private HotkeySettings _hotkeys = new();
    [ObservableProperty] private TranslationSettings _translation = new();
    [ObservableProperty] private ProcessingSettings _processing = new();
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
            UI = UI.DeepClone(),
            Update = Update.DeepClone(),
            Advanced = Advanced.DeepClone()
        };
    }
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

    public TranslationSettings DeepClone()
    {
        return new TranslationSettings
        {
            DefaultSourceLanguage = DefaultSourceLanguage,
            DefaultTargetLanguage = DefaultTargetLanguage,
            ActiveTranslationModelId = ActiveTranslationModelId,
            LanguagePairs = LanguagePairs.Select(pair => pair.DeepClone()).ToList()
        };
    }
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

    public AdvancedSettings DeepClone() => new()
    {
        ModelStoragePath = ModelStoragePath,
        InferenceThreads = InferenceThreads,
        LogLevel = LogLevel,
        HuggingFaceMirror = HuggingFaceMirror,
        HuggingFaceToken = HuggingFaceToken
    };
}
