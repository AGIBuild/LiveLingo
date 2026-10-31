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
        CloudLabel.Text = loc.T("settings.cloud");
        CloudHint.Text = loc.T("settings.cloud.hint");
        CloudTranslateBox.Content = loc.T("settings.cloud.translate");
        CloudCleanBox.Content = loc.T("settings.cloud.clean");
        CloudBaseUrlLabel.Text = loc.T("settings.cloud.baseUrl");
        CloudModelLabel.Text = loc.T("settings.cloud.model");
        CloudApiKeyLabel.Text = loc.T("settings.cloud.apiKey");
        CloudKeyStored.Text = loc.T("settings.cloud.keyStored");
        CloudForgetButton.Content = loc.T("settings.cloud.forget");
        CloudError.Text = loc.T("settings.cloud.invalid");
        LegacyRemoveButton.Content = loc.T("settings.legacy.remove");
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.LegacyCount) or nameof(SettingsViewModel.LegacyBytes))
                LegacyText.Text = loc.T("settings.legacy", vm.LegacyCount, (vm.LegacyBytes / 1_000_000_000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        };
        CancelButton.Content = loc.T("settings.cancel");
        SaveButton.Content = loc.T("settings.save");
        vm.CloseRequested += Close;
        Closed += (_, _) => vm.CloseRequested -= Close;
        Opened += async (_, _) => await vm.InitializeAsync();
    }
}
