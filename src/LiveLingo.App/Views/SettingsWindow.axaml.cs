using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiveLingo.App.Services.Localization;
using LiveLingo.App.ViewModels;

namespace LiveLingo.App.Views;

public partial class SettingsWindow : Window
{
    private PropertyChangedEventHandler? _propertyChangedHandler;

    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel vm, ILocalizationService? loc = null)
    {
        DataContext = vm;
        InitializeComponent();
        if (loc is not null) ApplyLocalization(loc);
        vm.RequestClose += () => Close();
        _propertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.OverlayOpacity))
                SetBackgroundOpacity(vm.OverlayOpacity);
        };
        vm.PropertyChanged += _propertyChangedHandler;
        SetBackgroundOpacity(vm.OverlayOpacity);
        Closed += (_, _) =>
        {
            if (_propertyChangedHandler is not null)
                vm.PropertyChanged -= _propertyChangedHandler;
        };
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

    private void SetBackgroundOpacity(double opacity)
    {
        var border = this.FindControl<ExperimentalAcrylicBorder>("AcrylicBg");
        if (border is null) return;

        border.Material = new ExperimentalAcrylicMaterial
        {
            BackgroundSource = AcrylicBackgroundSource.Digger,
            TintColor = Color.Parse("#0D0D0F"),
            TintOpacity = opacity,
            MaterialOpacity = opacity
        };
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
