using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Services.Localization;

namespace LiveLingo.Desktop.Settings;

/// <summary>Binding glue for <see cref="SettingsViewModel"/>; labels come from the localization service.</summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] Sections = ["general", "voice", "privacy", "models"];

    private readonly SettingsViewModel _vm;
    private readonly ILocalizationService _loc;

    public SettingsWindow()
    {
        throw new InvalidOperationException("SettingsWindow needs a view model.");
    }

    public SettingsWindow(SettingsViewModel vm, ILocalizationService loc, string section = "general")
    {
        InitializeComponent();
        _vm = vm;
        _loc = loc;
        Title = loc.T("settings.title");
        Avalonia.Automation.AutomationProperties.SetName(Nav, loc.T("settings.nav.aria"));
        NavGeneral.Content = loc.T("settings.nav.general");
        NavVoice.Content = loc.T("settings.nav.voice");
        NavPrivacy.Content = loc.T("settings.nav.privacy");
        NavModels.Content = loc.T("settings.nav.models");
        foreach (var button in new[] { NavGeneral, NavVoice, NavPrivacy, NavModels })
            button.Click += (_, _) => ShowSection((string)button.Tag!);

        Label(HotkeyTitle, HotkeyDesc, "settings.hotkey");
        HotkeyError.Text = loc.T("settings.hotkey.invalid");
        HotkeyBox.PromptText = loc.T("settings.hotkey.prompt");
        Label(CommitTitle, CommitDesc, "settings.commit");
        Fill(CommitBox, "settings.commit.enter", "settings.commit.ctrlEnter");
        Label(LanguageTitle, LanguageDesc, "settings.language");
        Label(ThemeTitle, ThemeDesc, "settings.theme");
        Fill(ThemeBox, "settings.theme.auto", "settings.theme.light", "settings.theme.dark");
        Label(MotionTitle, MotionDesc, "settings.motion");

        Label(MicTitle, MicDesc, "settings.mic");
        Fill(MicBox, "settings.mic.default");
        Label(DirectionTitle, DirectionDesc, "settings.direction");
        Fill(DirectionBox, "settings.direction.zhEn", "settings.direction.enZh");
        TermsTitle.Text = loc.T("settings.terms");
        TermSourceLabel.Text = loc.T("settings.terms.source");
        TermTargetLabel.Text = loc.T("settings.terms.target");
        TermSourceBox.PlaceholderText = "缓存穿透";
        TermTargetBox.PlaceholderText = "cache penetration";
        TermAddButton.Content = loc.T("settings.terms.add");
        TermsHint.Text = loc.T("settings.terms.hint");
        RemoveLabel = loc.T("settings.terms.remove");

        Label(CloudTranslateTitle, CloudTranslateDesc, "settings.cloud.translate");
        Label(CloudCleanTitle, CloudCleanDesc, "settings.cloud.clean");
        Label(CloudUrlTitle, CloudUrlDesc, "settings.cloud.baseUrl");
        Label(CloudModelTitle, CloudModelDesc, "settings.cloud.model");
        Label(CloudKeyTitle, CloudKeyDesc, "settings.cloud.apiKey");
        CloudKeyStored.Text = loc.T("settings.cloud.keyStored");
        CloudForgetButton.Content = loc.T("settings.cloud.forget");
        CloudError.Text = loc.T("settings.cloud.invalid");
        Label(RecentTitle, RecentDesc, "settings.recent");
        RecentClearButton.Content = loc.T("settings.recent.clear");

        foreach (var key in new[] { "settings.models.speech", "settings.models.clean", "settings.models.translate" })
        {
            ModelChips.Children.Add(new Border
            {
                Classes = { "chip" },
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                Child = new TextBlock { Text = loc.T(key), Classes = { "faint" }, VerticalAlignment = VerticalAlignment.Center }
            });
        }

        Label(ModelTitle, ModelDesc, "settings.model");
        Fill(ModelBox, "settings.model.quality", "settings.model.fast");
        LegacyTitle.Text = loc.T("settings.legacy");
        LegacyRemoveButton.Content = loc.T("settings.legacy.remove");
        Label(VersionTitle, VersionDesc, "settings.version");
        VersionText.Text = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "";

        Avalonia.Automation.AutomationProperties.SetName(CloudUrlBox, loc.T("settings.cloud.baseUrl"));
        Avalonia.Automation.AutomationProperties.SetName(CloudModelBox, loc.T("settings.cloud.model"));
        Avalonia.Automation.AutomationProperties.SetName(CloudKeyBox, loc.T("settings.cloud.apiKey"));
        Avalonia.Automation.AutomationProperties.SetName(HotkeyBox, loc.T("settings.hotkey"));
        Avalonia.Automation.AutomationProperties.SetName(MotionSwitch, loc.T("settings.motion"));
        Avalonia.Automation.AutomationProperties.SetName(CloudTranslateSwitch, loc.T("settings.cloud.translate"));
        Avalonia.Automation.AutomationProperties.SetName(CloudCleanSwitch, loc.T("settings.cloud.clean"));

        CloudTranslateSwitch.IsChecked = vm.CloudTranslate;
        CloudCleanSwitch.IsChecked = vm.CloudClean;
        CloudTranslateSwitch.IsCheckedChanged += async (_, _) => await OnCloudSwitchAsync(CloudTranslateSwitch, translation: true);
        CloudCleanSwitch.IsCheckedChanged += async (_, _) => await OnCloudSwitchAsync(CloudCleanSwitch, translation: false);
        CloudKeyBox.LostFocus += async (_, _) => await _vm.SaveCloudKeyCommand.ExecuteAsync(null);
        CloudKeyBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await _vm.SaveCloudKeyCommand.ExecuteAsync(null);
        };

        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SettingsViewModel.LegacyCount) or nameof(SettingsViewModel.LegacyBytes):
                    LegacyDesc.Text = loc.T("settings.legacy.desc", vm.LegacyCount, FormatGb(vm.LegacyBytes));
                    break;
                case nameof(SettingsViewModel.MicrophoneNames):
                    FillMicrophones();
                    break;
                case nameof(SettingsViewModel.CloudTranslate):
                    CloudTranslateSwitch.IsChecked = vm.CloudTranslate;
                    break;
                case nameof(SettingsViewModel.CloudClean):
                    CloudCleanSwitch.IsChecked = vm.CloudClean;
                    break;
            }
        };
        Opened += async (_, _) => await vm.InitializeAsync();

        // The combo boxes have their items now, so the bindings can take their selected indexes.
        DataContext = vm;
        ShowSection(section);
    }

    /// <summary>The label of the remove button in the glossary rows.</summary>
    public string RemoveLabel { get; } = "";

    public void ShowSection(string section)
    {
        var index = Math.Max(0, Array.IndexOf(Sections, section));
        PanelGeneral.IsVisible = index == 0;
        PanelVoice.IsVisible = index == 1;
        PanelPrivacy.IsVisible = index == 2;
        PanelModels.IsVisible = index == 3;
        NavGeneral.Classes.Set("active", index == 0);
        NavVoice.Classes.Set("active", index == 1);
        NavPrivacy.Classes.Set("active", index == 2);
        NavModels.Classes.Set("active", index == 3);
    }

    private void Label(TextBlock title, TextBlock description, string key)
    {
        title.Text = _loc.T(key);
        description.Text = _loc.T(key + ".desc");
    }

    private void Fill(ComboBox box, params string[] keys)
    {
        foreach (var key in keys)
            box.Items.Add(new ComboBoxItem { Content = _loc.T(key) });
    }

    /// <summary>"System default" first, then every input device by name.</summary>
    private void FillMicrophones()
    {
        MicBox.Items.Clear();
        foreach (var name in _vm.MicrophoneNames)
            MicBox.Items.Add(new ComboBoxItem { Content = name.Length == 0 ? _loc.T("settings.mic.default") : name });
        MicBox.SelectedIndex = _vm.MicrophoneIndex;
    }

    private static string FormatGb(long bytes) =>
        (bytes / 1_000_000_000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Turning a cloud step on needs the user's consent each time; turning it off is immediate.</summary>
    private async Task OnCloudSwitchAsync(ToggleSwitch toggle, bool translation)
    {
        var enabled = toggle.IsChecked == true;
        var stored = translation ? _vm.CloudTranslate : _vm.CloudClean;
        if (enabled == stored)
            return;

        if (!enabled)
        {
            _vm.DisableCloud(translation);
            return;
        }

        var allowed = await AskConsentAsync(translation);
        if (!allowed || !_vm.TryEnableCloud(translation))
            toggle.IsChecked = false;
    }

    private Task<bool> AskConsentAsync(bool translation)
    {
        var result = new TaskCompletionSource<bool>();
        var keepLocal = new Button { Classes = { "btn" }, Content = _loc.T("settings.consent.keepLocal") };
        var allow = new Button { Classes = { "btn", "primary" }, Content = _loc.T("settings.consent.allow") };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Children = { keepLocal, allow }
        };
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = _loc.T("settings.consent.body"), Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        var dialog = CreateDialog(_loc.T(translation ? "settings.consent.translate" : "settings.consent.clean"), body);
        keepLocal.Click += (_, _) => dialog.Close();
        allow.Click += (_, _) =>
        {
            result.TrySetResult(true);
            dialog.Close();
        };
        dialog.Closed += (_, _) => result.TrySetResult(false);
        dialog.ShowDialog(this);
        return result.Task;
    }

    /// <summary>A rounded panel with a title and a close button. ponytail: native dialog until the settings move to the web view (W5), which brings its own.</summary>
    private static Window CreateDialog(string title, Control body)
    {
        var close = new Button { Classes = { "icon" }, Content = new Controls.Icon { Data = (Avalonia.Media.Geometry)Avalonia.Application.Current!.FindResource("IcClose")! } };
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new Avalonia.Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, Classes = { "heading" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(body);

        var panel = new Border
        {
            Margin = new Avalonia.Thickness(18),
            Padding = new Avalonia.Thickness(22),
            CornerRadius = new Avalonia.CornerRadius(18),
            BorderThickness = new Avalonia.Thickness(1),
            Child = content
        };
        panel.Bind(Border.BackgroundProperty, panel.GetResourceObservable("Panel"));
        panel.Bind(Border.BorderBrushProperty, panel.GetResourceObservable("Line"));
        panel.Bind(Border.BoxShadowProperty, panel.GetResourceObservable("TkShadow"));

        var window = new Window
        {
            Width = 596,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        close.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                window.Close();
        };
        return window;
    }

    private void OnRemoveTermClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: TermEntry term })
            _vm.RemoveTermCommand.Execute(term);
    }
}
