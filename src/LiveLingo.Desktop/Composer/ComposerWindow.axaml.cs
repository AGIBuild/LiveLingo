using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveLingo.Desktop.Platform;
using System.ComponentModel;

namespace LiveLingo.Desktop.Composer;

internal sealed record ComposerLineView(
    int Index,
    string Text,
    bool IsPending,
    bool IsFailed,
    string RetryLabel,
    string Time)
{
    public bool HasTime => Time.Length > 0;
}

/// <summary>Binding glue for <see cref="ComposerViewModel"/>: maps view-model state to controls and keys to commands.</summary>
public partial class ComposerWindow : Window
{
    private const double NarrowWidth = 680;
    private const double WideWidth = 980;

    private readonly ComposerViewModel _vm;
    private readonly ComposerTexts _texts;
    private readonly TargetWindowInfo? _target;
    private readonly string _retryLabel;
    private readonly RecentDrafts _recent;
    private bool _wide;

    public ComposerWindow()
    {
        throw new InvalidOperationException("ComposerWindow needs a view model.");
    }

    public ComposerWindow(ComposerViewModel vm, ComposerTexts texts, TargetWindowInfo? target, RecentDrafts recent)
    {
        InitializeComponent();
        _vm = vm;
        _texts = texts;
        _target = target;
        _recent = recent;
        _retryLabel = texts.RetryLabel;
        DataContext = vm;

        RecentButton.Content = texts.RecentLabel;
        RecentButton.IsVisible = recent.Items.Count > 0;
        CopyButton.Content = texts.CopyLabel;
        WriteButton.Content = texts.WriteLabel;
        CleanButton.Content = texts.CleanLabel;
        OriginalButton.Content = texts.OriginalLabel;
        ImportButton.Content = texts.ImportLabel;
        ExportButton.Content = texts.ExportLabel;
        SourceBox.Watermark = texts.Placeholder;
        Avalonia.Controls.ToolTip.SetTip(MicButton, texts.MicLabel);
        Avalonia.Automation.AutomationProperties.SetName(MicButton, texts.MicLabel);

        MicButton.AddHandler(PointerPressedEvent, OnMicPressed, RoutingStrategies.Tunnel);
        MicButton.AddHandler(PointerReleasedEvent, OnMicReleased, RoutingStrategies.Tunnel);
        MicButton.AddHandler(KeyDownEvent, OnMicKeyDown, RoutingStrategies.Tunnel);
        vm.PropertyChanged += OnViewModelChanged;
        vm.Completed += Close;
        vm.CloseRequested += Close;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            Place();
            Activate();
            SourceBox.Focus();
        };
        Closed += (_, _) =>
        {
            vm.PropertyChanged -= OnViewModelChanged;
            vm.Completed -= Close;
            vm.CloseRequested -= Close;
        };
        Refresh();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        TargetText.Text = _texts.Target(_vm.TargetName, _vm.HasTarget);
        TranslateButton.Content = _texts.TranslateLabel(_vm.TargetLanguage);
        CleanButton.Classes.Set("active", _vm.Output == ComposerOutput.Clean);
        TranslateButton.Classes.Set("active", _vm.Output == ComposerOutput.Translate);
        OriginalButton.Classes.Set("active", _vm.Output == ComposerOutput.Original);
        SwapButton.IsVisible = _vm.Output == ComposerOutput.Translate;
        MicButton.Classes.Set("listening", _vm.IsListening || _vm.IsStartingListening);

        StatusText.Text = _texts.Status(_vm.Status, _vm.IsWriteQueued, _vm.KeptOriginalCount, (int)_vm.TranscribeProgress);
        CopyButton.IsEnabled = _vm.CanCopy;
        WriteButton.IsEnabled = _vm.CanWrite || _vm.IsWriteQueued;

        var banner = _vm.Banner;
        BannerBox.IsVisible = banner.Kind != BannerKind.None;
        BannerText.Text = _texts.BannerMessage(banner);
        var action = _texts.BannerAction(banner);
        BannerAction.IsVisible = action.Length > 0;
        BannerAction.Content = action;
        BannerProgress.IsVisible = banner.Kind == BannerKind.Downloading;
        BannerProgress.Value = banner.Progress;

        ResultBox.IsVisible = _vm.Lines.Count > 0;
        // A finished result is one selectable block, so any part (or all) of what will be written can be copied.
        // A timeline keeps its per-line times instead.
        var asBlock = _vm.ResultText.Length > 0 && !_vm.HasTimeline;
        ResultTextBlock.Text = _vm.ResultText;
        ResultTextBlock.IsVisible = asBlock;
        ResultList.IsVisible = !asBlock;
        ResultList.ItemsSource = _vm.Lines.Select(ToView).ToList();
        ApplyLayout(_vm.IsWide);
    }

    /// <summary>A whole document or transcript puts the source and the result side by side; otherwise they stack.</summary>
    private void ApplyLayout(bool wide)
    {
        if (wide == _wide)
            return;

        _wide = wide;
        BodyGrid.ColumnDefinitions = ColumnDefinitions.Parse(wide ? "*,*" : "*");
        Grid.SetColumn(ResultBox, wide ? 1 : 0);
        Grid.SetRow(ResultBox, wide ? 0 : 1);
        ResultBox.Margin = wide ? new Thickness(10, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        SourceBox.MaxHeight = wide ? 320 : 160;
        Width = wide ? WideWidth : NarrowWidth;
        if (IsVisible)
            Place();
    }

    private ComposerLineView ToView(ResultLine line)
    {
        return new ComposerLineView(
            line.Index,
            _texts.Line(line),
            line.State == LineState.Pending,
            line.State == LineState.Failed,
            _retryLabel,
            line.Time);
    }

    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var draft in _recent.Items)
        {
            var item = new MenuItem { Header = ComposerTexts.RecentItem(draft), IsEnabled = _vm.CanReplaceDraft };
            item.Click += (_, _) => _vm.SourceText = draft.Text;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = _texts.RecentClearLabel };
        clear.Click += (_, _) =>
        {
            _recent.Clear();
            RecentButton.IsVisible = false;
        };
        menu.Items.Add(clear);
        menu.ShowAt(RecentButton);
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _texts.ImportTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(_texts.ImportFileType) { Patterns = ["*.wav"] }]
        });
        if (files.Count > 0)
            await _vm.ImportAsync(files[0].Path.LocalPath);
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _texts.ExportTitle,
            SuggestedFileName = "transcript.srt",
            DefaultExtension = "srt",
            FileTypeChoices = [new FilePickerFileType("SubRip") { Patterns = ["*.srt"] }]
        });
        if (file is not null)
            await _vm.ExportSrtAsync(file.Path.LocalPath);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) && SourceBox.IsFocused)
        {
            _vm.WriteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCleanClick(object? sender, RoutedEventArgs e) => _vm.Output = ComposerOutput.Clean;

    private async void OnMicPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        await _vm.PressMicAsync();
    }

    private async void OnMicReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
        await _vm.ReleaseMicAsync();
    }

    private async void OnMicKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter))
            return;

        e.Handled = true;
        await _vm.ToggleListeningCommand.ExecuteAsync(null);
    }

    private void OnTranslateClick(object? sender, RoutedEventArgs e) => _vm.Output = ComposerOutput.Translate;

    private void OnOriginalClick(object? sender, RoutedEventArgs e) => _vm.Output = ComposerOutput.Original;

    private void OnSwapClick(object? sender, RoutedEventArgs e) => _vm.SwapLanguageCommand.Execute(null);

    private void OnBannerActionClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.Banner.Kind == BannerKind.MicrophoneDenied)
            _vm.OpenMicrophoneSettingsCommand.Execute(null);
        else
            _vm.DownloadModelsCommand.Execute(null);
    }

    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: int index })
            _vm.RetryCommand.Execute(index);
    }

    private void Place()
    {
        if (_target is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var screen = Screens.ScreenFromPoint(new PixelPoint(_target.Left + _target.Width / 2, _target.Top + _target.Height / 2))
            ?? Screens.Primary;
        if (screen is null)
            return;

        var work = screen.WorkingArea;
        var (x, y) = ComposerPlacement.For(
            _target, Width, Bounds.Height > 0 ? Bounds.Height : 260, screen.Scaling,
            new PixelBounds(work.X, work.Y, work.Width, work.Height));
        Position = new PixelPoint(x, y);
    }
}
