namespace LiveLingo.App.Services.Configuration;

public record UserSettings
{
    public HotkeySettings Hotkeys { get; init; } = new();
    public TranslationSettings Translation { get; init; } = new();
    public ProcessingSettings Processing { get; init; } = new();
    public UISettings UI { get; init; } = new();
    public UpdateSettings Update { get; init; } = new();
    public AdvancedSettings Advanced { get; init; } = new();
}

public record HotkeySettings
{
    public string OverlayToggle { get; init; } = "Ctrl+Alt+T";
}

public record TranslationSettings
{
    public string DefaultSourceLanguage { get; init; } = "zh";
    public string DefaultTargetLanguage { get; init; } = "en";
    public List<LanguagePair> LanguagePairs { get; init; } = [new("zh", "en")];
}

public record LanguagePair(string Source, string Target);

public record ProcessingSettings
{
    public string DefaultMode { get; init; } = "Off";
}

public record UISettings
{
    public double OverlayOpacity { get; init; } = 0.95;
    public string DefaultInjectionMode { get; init; } = "PasteAndSend";
    public string Language { get; init; } = "en-US";
    public OverlayPosition? LastOverlayPosition { get; init; }
}

public record OverlayPosition(int X, int Y);

public record UpdateSettings
{
    public string UpdateUrl { get; init; } = "";
    public int CheckIntervalHours { get; init; } = 4;
}

public record AdvancedSettings
{
    public string? ModelStoragePath { get; init; }
    public int InferenceThreads { get; init; } = 0;
    public string LogLevel { get; init; } = "Information";
}
