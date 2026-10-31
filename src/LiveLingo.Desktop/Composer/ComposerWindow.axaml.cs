using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveLingo.Desktop.Platform;
using System.ComponentModel;

namespace LiveLingo.Desktop.Composer;

internal sealed record ComposerLineView(
    int Index,
    string Text,
    bool IsPending,
    bool IsFailed,
    string RetryLabel);

/// <summary>Binding glue for <see cref="ComposerViewModel"/>: maps view-model state to controls and keys to commands.</summary>
public partial class ComposerWindow : Window
{
    private readonly ComposerViewModel _vm;
    private readonly ComposerTexts _texts;
    private readonly TargetWindowInfo? _target;
    private readonly string _retryLabel;

    public ComposerWindow()
    {
        throw new InvalidOperationException("ComposerWindow needs a view model.");
    }

    public ComposerWindow(ComposerViewModel vm, ComposerTexts texts, TargetWindowInfo? target)
    {
        InitializeComponent();
        _vm = vm;
        _texts = texts;
        _target = target;
        _retryLabel = texts.RetryLabel;
        DataContext = vm;

        CopyButton.Content = texts.CopyLabel;
        WriteButton.Content = texts.WriteLabel;
        CleanButton.Content = texts.CleanLabel;
        OriginalButton.Content = texts.OriginalLabel;
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

        StatusText.Text = _texts.Status(_vm.Status, _vm.IsWriteQueued, _vm.KeptOriginalCount);
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
        ResultTextBlock.Text = _vm.ResultText;
        ResultTextBlock.IsVisible = _vm.ResultText.Length > 0;
        ResultList.IsVisible = _vm.ResultText.Length == 0;
        ResultList.ItemsSource = _vm.Lines.Select(ToView).ToList();
    }

    private ComposerLineView ToView(ResultLine line)
    {
        return new ComposerLineView(
            line.Index,
            _texts.Line(line),
            line.State == LineState.Pending,
            line.State == LineState.Failed,
            _retryLabel);
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
