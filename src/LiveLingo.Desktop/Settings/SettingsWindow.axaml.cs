using Avalonia.Controls;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Settings;

/// <summary>Binding glue for <see cref="SettingsViewModel"/>; labels come from the localization service.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        throw new InvalidOperationException("SettingsWindow needs a view model.");
    }

    public SettingsWindow(SettingsViewModel vm, ILocalizationService loc)
    {
        InitializeComponent();
        DataContext = vm;
        Title = loc.T("settings.title");
        HotkeyLabel.Text = loc.T("settings.hotkey");
        HotkeyHint.Text = loc.T("settings.hotkey.hint");
        HotkeyError.Text = loc.T("settings.hotkey.invalid");
        LanguageLabel.Text = loc.T("settings.language");
        QualityBox.Content = new TextBlock { Text = loc.T("settings.quality"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 470 };
        GlossaryLabel.Text = loc.T("settings.glossary");
        GlossaryHint.Text = loc.T("settings.glossary.hint");
        CancelButton.Content = loc.T("settings.cancel");
        SaveButton.Content = loc.T("settings.save");
        vm.CloseRequested += Close;
        Closed += (_, _) => vm.CloseRequested -= Close;
    }
}
