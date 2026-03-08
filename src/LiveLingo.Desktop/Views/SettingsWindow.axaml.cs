using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.ViewModels;

namespace LiveLingo.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel vm, ILocalizationService? loc = null)
    {
        DataContext = vm;
        InitializeComponent();
        if (loc is not null) ApplyLocalization(loc);
        vm.RequestClose += () => Close();
        vm.RequestShowPermissionCheck += ShowPermissionDialog;
        Closed += (_, _) =>
        {
            vm.RequestShowPermissionCheck -= ShowPermissionDialog;
        };
    }

    private void ShowPermissionDialog()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var ax = Platform.macOS.AccessibilityPermission.IsGranted();
        var im = Platform.macOS.AccessibilityPermission.IsInputMonitoringGranted();
        var allGranted = ax && im;

        var bgSurface = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1C1C1E"));
        var bgElevated = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A2C"));
        var borderSubtle = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A3C"));
        var fgPrimary = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E5E5E7"));
        var fgSecondary = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C7C7CC"));
        var fgMuted = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8E8E93"));
        var accent = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A84FF"));
        var successBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#30D158"));
        var dangerBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF453A"));

        var dialog = new Window
        {
            Title = "Permission Check",
            Width = 380,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            CanResize = false,
            Background = bgSurface,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.Full
        };

        var root = new StackPanel { Margin = new Avalonia.Thickness(24, 20), Spacing = 16 };

        root.Children.Add(new TextBlock
        {
            Text = "macOS Permissions",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = fgPrimary
        });

        root.Children.Add(BuildPermissionRow("Accessibility", ax, bgElevated, borderSubtle, fgPrimary, fgMuted, successBrush, dangerBrush, accent,
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"));
        root.Children.Add(BuildPermissionRow("Input Monitoring", im, bgElevated, borderSubtle, fgPrimary, fgMuted, successBrush, dangerBrush, accent,
            "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"));

        var hint = new TextBlock
        {
            Text = allGranted
                ? "All permissions granted. Global hotkey should work."
                : "Add the terminal app (Terminal / Cursor / iTerm) to both lists, then restart.",
            Foreground = allGranted ? fgMuted : dangerBrush,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };
        root.Children.Add(hint);

        var footer = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var recheck = new Button
        {
            Content = "↻ Recheck",
            Background = bgElevated,
            Foreground = fgPrimary,
            BorderBrush = borderSubtle,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(14, 7)
        };
        recheck.Click += (_, _) => { dialog.Close(); ShowPermissionDialog(); };
        footer.Children.Add(recheck);

        var close = new Button
        {
            Content = "Done",
            Background = accent,
            Foreground = Avalonia.Media.Brushes.White,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(20, 7)
        };
        close.Click += (_, _) => dialog.Close();
        footer.Children.Add(close);

        root.Children.Add(footer);
        dialog.Content = root;
        dialog.ShowDialog(this);
    }

    private static Border BuildPermissionRow(
        string label, bool granted,
        Avalonia.Media.IBrush bg, Avalonia.Media.IBrush border,
        Avalonia.Media.IBrush fgPrimary, Avalonia.Media.IBrush fgMuted,
        Avalonia.Media.IBrush successBrush, Avalonia.Media.IBrush dangerBrush,
        Avalonia.Media.IBrush accent, string settingsUrl)
    {
        var row = new Border
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(14, 10)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        var indicator = new TextBlock
        {
            Text = granted ? "✓" : "✗",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = granted ? successBrush : dangerBrush,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(indicator, 0);
        grid.Children.Add(indicator);

        var info = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            Foreground = fgPrimary
        });
        info.Children.Add(new TextBlock
        {
            Text = granted ? "Granted" : "Not Granted",
            FontSize = 11,
            Foreground = granted ? fgMuted : dangerBrush
        });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (!granted)
        {
            var openBtn = new Button
            {
                Content = "Open Settings →",
                FontSize = 12,
                Background = Avalonia.Media.Brushes.Transparent,
                Foreground = accent,
                BorderThickness = new Avalonia.Thickness(0),
                Padding = new Avalonia.Thickness(8, 4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            openBtn.Click += (_, _) => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = settingsUrl,
                    UseShellExecute = false
                });
            Grid.SetColumn(openBtn, 2);
            grid.Children.Add(openBtn);
        }

        row.Child = grid;
        return row;
    }

    private void ApplyLocalization(ILocalizationService loc)
    {
        Title = loc.T("settings.title");
        SetText("TitleText", loc.T("settings.title"));
        SetContent("ResetBtn", loc.T("settings.resetDefaults"));
        SetContent("CancelBtn", loc.T("settings.cancel"));
        SetContent("SaveBtn", loc.T("settings.save"));
        SetHeader("TabGeneral", loc.T("settings.tab.general"));
        SetHeader("TabTranslation", loc.T("settings.tab.translation"));
        SetHeader("TabModels", loc.T("settings.tab.models"));
        SetHeader("TabAdvanced", loc.T("settings.tab.advanced"));
        SetHeader("TabAI", loc.T("settings.tab.ai"));
    }

    private void SetText(string name, string text)
    {
        if (this.FindControl<TextBlock>(name) is { } tb) tb.Text = text;
    }

    private void SetContent(string name, string text)
    {
        if (this.FindControl<Button>(name) is { } btn) btn.Content = text;
    }

    private void SetHeader(string name, string text)
    {
        if (this.FindControl<TabItem>(name) is { } tab) tab.Header = text;
    }

    public void SetBackgroundOpacity(double opacity)
    {
        var border = this.FindControl<Border>("BgBorder");
        if (border is null) return;

        var baseColor = Color.Parse("#1C1C1E");
        var alpha = (byte)(opacity * 255);
        border.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    public async void BrowseModelPath_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Model Storage Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is SettingsViewModel vm)
            vm.ModelStoragePath = folders[0].Path.LocalPath;
    }
}
