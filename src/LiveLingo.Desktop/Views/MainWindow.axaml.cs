using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly ILocalizationService? _loc;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(ILocalizationService? localizationService)
    {
        _loc = localizationService;
        InitializeComponent();
        ApplyLocalization();
    }

    public void SetHotkeyDisplay(string hotkey)
    {
        var display = this.FindControl<TextBlock>("HotkeyDisplay");
        if (display is not null)
            display.Text = $"⌨  {hotkey}";
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.ShowSettings();
    }

    private void ApplyLocalization()
    {
        Title = L("main.window.title", "LiveLingo");
        if (this.FindControl<TextBlock>("MainTitleText") is { } title)
            title.Text = L("app.name", "LiveLingo");
        if (this.FindControl<TextBlock>("MainHintPrimaryText") is { } primaryHint)
            primaryHint.Text = L("main.hint.primary", "Global hotkey registered. Press the shortcut while any app is focused to open the translation overlay.");
        if (this.FindControl<TextBlock>("MainHintSecondaryText") is { } secondaryHint)
            secondaryHint.Text = L("main.hint.secondary", "The overlay pops up over the active window.");
        if (this.FindControl<Button>("MainSettingsButton") is { } settingsButton)
            settingsButton.Content = $"⚙ {L("tray.settings", "Settings")}";
    }

    private string L(string key, string fallback) => _loc?.T(key) ?? fallback;
}
