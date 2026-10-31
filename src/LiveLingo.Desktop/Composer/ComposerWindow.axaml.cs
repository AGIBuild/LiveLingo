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

    public ComposerWindow(
        ComposerViewModel vm,
        ComposerTexts texts,
        TargetWindowInfo? target,
        string copyLabel,
        string writeLabel,
        string originalLabel,
        string placeholder,
        string retryLabel)
    {
        InitializeComponent();
        _vm = vm;
        _texts = texts;
        _target = target;
        _retryLabel = retryLabel;
        DataContext = vm;

        CopyButton.Content = copyLabel;
        WriteButton.Content = writeLabel;
        OriginalButton.Content = originalLabel;
        SourceBox.Watermark = placeholder;

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
        var translating = _vm.Output == ComposerOutput.Translate;
        TranslateButton.Classes.Set("active", translating);
        OriginalButton.Classes.Set("active", !translating);
        SwapButton.IsVisible = translating;

        StatusText.Text = _texts.Status(_vm.Status, _vm.IsWriteQueued);
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

    private void OnTranslateClick(object? sender, RoutedEventArgs e) => _vm.Output = ComposerOutput.Translate;

    private void OnOriginalClick(object? sender, RoutedEventArgs e) => _vm.Output = ComposerOutput.Original;

    private void OnSwapClick(object? sender, RoutedEventArgs e) => _vm.SwapLanguageCommand.Execute(null);

    private void OnBannerActionClick(object? sender, RoutedEventArgs e) => _vm.DownloadModelsCommand.Execute(null);

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
