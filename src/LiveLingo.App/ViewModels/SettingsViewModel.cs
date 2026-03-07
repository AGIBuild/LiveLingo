using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Services.Configuration;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;

namespace LiveLingo.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IModelManager? _modelManager;
    private readonly ILogger? _logger;
    private string? _originalModelStoragePath;

    [ObservableProperty] private string _overlayHotkey = string.Empty;
    [ObservableProperty] private string _defaultSourceLanguage = string.Empty;
    [ObservableProperty] private string _defaultTargetLanguage = string.Empty;
    [ObservableProperty] private string _defaultPostProcessMode = string.Empty;
    [ObservableProperty] private string _defaultInjectionMode = string.Empty;
    [ObservableProperty] private double _overlayOpacity;
    [ObservableProperty] private string? _modelStoragePath;
    [ObservableProperty] private int _inferenceThreads;
    [ObservableProperty] private string _logLevel = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _migrationError;

    [ObservableProperty] private UILanguageOption _selectedUILanguage = UILanguages[0];

    public static IReadOnlyList<string> InjectionModes { get; } = ["PasteAndSend", "PasteOnly"];
    public static IReadOnlyList<string> PostProcessModes { get; } = ["Off", "Summarize", "Optimize", "Colloquialize"];
    public static IReadOnlyList<string> LogLevels { get; } = ["Verbose", "Debug", "Information", "Warning", "Error"];
    public static IReadOnlyList<UILanguageOption> UILanguages { get; } =
        [new("en-US", "English"), new("zh-CN", "简体中文")];

    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    [ObservableProperty] private LanguageInfo? _selectedSourceLanguage;
    [ObservableProperty] private LanguageInfo? _selectedTargetLanguage;

    public ObservableCollection<ModelItemViewModel> Models { get; }

    public event Action? RequestClose;

    public SettingsViewModel(ISettingsService settings, IModelManager modelManager,
        ITranslationEngine? engine = null, ILogger<SettingsViewModel>? logger = null)
    {
        _settings = settings;
        _modelManager = modelManager;
        _logger = logger;
        AvailableLanguages = engine?.SupportedLanguages ?? [];
        Models = ModelItemViewModel.CreateAll(modelManager);
        LoadFromSettings(_settings.Current);
    }

    public SettingsViewModel(ISettingsService settings, ITranslationEngine? engine = null)
    {
        _settings = settings;
        AvailableLanguages = engine?.SupportedLanguages ?? [];
        Models = [];
        LoadFromSettings(_settings.Current);
    }

    private void LoadFromSettings(UserSettings s)
    {
        OverlayHotkey = s.Hotkeys.OverlayToggle;
        DefaultSourceLanguage = s.Translation.DefaultSourceLanguage;
        DefaultTargetLanguage = s.Translation.DefaultTargetLanguage;
        SelectedSourceLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, s.Translation.DefaultSourceLanguage, StringComparison.OrdinalIgnoreCase));
        SelectedTargetLanguage = AvailableLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, s.Translation.DefaultTargetLanguage, StringComparison.OrdinalIgnoreCase));
        DefaultPostProcessMode = s.Processing.DefaultMode;
        DefaultInjectionMode = s.UI.DefaultInjectionMode;
        OverlayOpacity = s.UI.OverlayOpacity;
        SelectedUILanguage = UILanguages.FirstOrDefault(l =>
            string.Equals(l.Code, s.UI.Language, StringComparison.OrdinalIgnoreCase)) ?? UILanguages[0];
        ModelStoragePath = s.Advanced.ModelStoragePath;
        InferenceThreads = s.Advanced.InferenceThreads;
        LogLevel = s.Advanced.LogLevel;
        _originalModelStoragePath = s.Advanced.ModelStoragePath;
        IsDirty = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(IsDirty)))
            IsDirty = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!IsDirty)
        {
            RequestClose?.Invoke();
            return;
        }

        MigrationError = null;
        var oldPath = NormalizePathForCompare(_originalModelStoragePath);
        var newPath = NormalizePathForCompare(ModelStoragePath);
        if (_modelManager is not null && !string.IsNullOrEmpty(newPath) &&
            !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _modelManager.MigrateStoragePathAsync(newPath);
                _originalModelStoragePath = ModelStoragePath;
            }
            catch (Exception ex)
            {
                MigrationError = $"Migration failed: {ex.Message}";
                _logger?.LogError(ex, "Failed to migrate model storage path");
                return;
            }
        }

        _settings.Update(s => s with
        {
            Hotkeys = s.Hotkeys with { OverlayToggle = OverlayHotkey },
            Translation = s.Translation with
            {
                DefaultSourceLanguage = SelectedSourceLanguage?.Code ?? DefaultSourceLanguage,
                DefaultTargetLanguage = SelectedTargetLanguage?.Code ?? DefaultTargetLanguage
            },
            Processing = s.Processing with { DefaultMode = DefaultPostProcessMode },
            UI = s.UI with
            {
                DefaultInjectionMode = DefaultInjectionMode,
                OverlayOpacity = OverlayOpacity,
                Language = SelectedUILanguage.Code
            },
            Advanced = s.Advanced with
            {
                ModelStoragePath = ModelStoragePath,
                InferenceThreads = InferenceThreads,
                LogLevel = LogLevel
            }
        });

        IsDirty = false;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Reset()
    {
        LoadFromSettings(new UserSettings());
    }

    [RelayCommand]
    private void Cancel()
    {
        LoadFromSettings(_settings.Current);
        RequestClose?.Invoke();
    }

    private static string NormalizePathForCompare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}

public record UILanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
