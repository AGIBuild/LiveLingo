using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LiveLingo.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
