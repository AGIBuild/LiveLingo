using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveLingo.App.Services.Configuration;

namespace LiveLingo.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

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

    public event Action? RequestClose;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        LoadFromSettings(_settings.Current);
    }

    private void LoadFromSettings(UserSettings s)
    {
        OverlayHotkey = s.Hotkeys.OverlayToggle;
        DefaultSourceLanguage = s.Translation.DefaultSourceLanguage;
        DefaultTargetLanguage = s.Translation.DefaultTargetLanguage;
        DefaultPostProcessMode = s.Processing.DefaultMode;
        DefaultInjectionMode = s.UI.DefaultInjectionMode;
        OverlayOpacity = s.UI.OverlayOpacity;
        ModelStoragePath = s.Advanced.ModelStoragePath;
        InferenceThreads = s.Advanced.InferenceThreads;
        LogLevel = s.Advanced.LogLevel;
        IsDirty = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(IsDirty)))
            IsDirty = true;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Update(s => s with
        {
            Hotkeys = s.Hotkeys with { OverlayToggle = OverlayHotkey },
            Translation = s.Translation with
            {
                DefaultSourceLanguage = DefaultSourceLanguage,
                DefaultTargetLanguage = DefaultTargetLanguage
            },
            Processing = s.Processing with { DefaultMode = DefaultPostProcessMode },
            UI = s.UI with
            {
                DefaultInjectionMode = DefaultInjectionMode,
                OverlayOpacity = OverlayOpacity
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
}
