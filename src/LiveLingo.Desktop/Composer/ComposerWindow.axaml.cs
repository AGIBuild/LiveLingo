using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LiveLingo.Desktop.Platform;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace LiveLingo.Desktop.Composer;

internal sealed record SourceLineView(string Time, string Text, bool IsTail)
{
    public bool HasTime => Time.Length > 0;
}

/// <summary>One sentence of the result pane with everything its template shows.</summary>
internal sealed record ComposerRowView(
    ComposerViewModel Vm,
    int Index,
    string Text,
    LineState State,
    string Error,
    string Time,
    string Note,
    bool IsWarn,
    bool IsOwned,
    bool IsEditing,
    bool ShowTools,
    bool CanUseOriginal,
    string CommitKeyLabel,
    ComposerTexts Texts)
{
    public bool IsReady => State == LineState.Ready && !IsEditing;
    public bool IsPending => State == LineState.Pending && !IsEditing;
    public bool IsFailed => State == LineState.Failed && !IsEditing;
    public bool IsEmpty => State == LineState.Empty && !IsEditing;
    public bool HasNote => Note.Length > 0 && !IsEditing;
    public bool HasTime => Time.Length > 0;
    public Thickness Separator => Index > 0 ? new Thickness(0, 1, 0, 0) : new Thickness(0);

    public string RetryLabel => Texts.RowRetry;
    public string ManualLabel => Texts.RowManual;
    public string SaveLabel => Texts.RowSave;
    public string CancelLabel => Texts.RowCancel;
    public string EmptyLabel => Texts.Line(new ResultLine(Index, "", LineState.Empty, ""));
    public string UseOriginalLabel => Texts.RowUseOriginal;
    public string EditLabel => Texts.RowEdit;
    public string UseOriginalAria => Texts.RowUseOriginalAria(Index);
    public string EditAria => Texts.RowEditAria(Index);

    public string EditText
    {
        get => Vm.EditText;
        set => Vm.EditText = value;
    }
}

/// <summary>Binding glue for <see cref="ComposerViewModel"/>: maps view-model state to controls and keys to commands.</summary>
public partial class ComposerWindow : Window
{
    private const double NarrowPanelWidth = 620;
    private const double WidePanelWidth = 940;
    private const double ShadowMargin = 36;

    private readonly ComposerViewModel _vm;
    private readonly ComposerTexts _texts;
    private readonly TargetWindowInfo? _target;
    private readonly RecentDrafts _recent;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3.6) };
    private bool _wide;
    private List<ComposerRowView> _rows = [];
    private int _editFocusedFor = -1;

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
        DataContext = vm;
        Classes.Set("still", !vm.Animations);

        SourceBox.PlaceholderText = texts.Placeholder;
        HintMic.Text = texts.HintMic;
        HintWrite.Text = texts.HintWrite;
        HintNewline.Text = texts.HintNewline;
        HintHide.Text = texts.HintHide;
        HintCommitKey.Text = ComposerTexts.CommitKeyLabel(vm.CommitKey);
        HintNewlineKey.Text = ComposerTexts.NewlineKeyLabel;
        RouteChipText.Text = texts.RouteChip;
        Describe(DestButton, texts.DestAria);
        Describe(OutputPill, texts.OutputAria);
        Describe(MoreButton, texts.MoreAria);
        Describe(CloseButton, texts.CloseAria);
        Describe(CopyButton, texts.CopyAria);
        Describe(SourceBox, texts.SourceLabel(false, false, ""));
        Avalonia.Controls.ToolTip.SetTip(CloseButton, texts.CloseAria);
        Avalonia.Controls.ToolTip.SetTip(MoreButton, texts.MoreAria);
        Avalonia.Controls.ToolTip.SetTip(CopyButton, texts.CopyAria);
        Avalonia.Automation.AutomationProperties.SetLiveSetting(StatusText, Avalonia.Automation.AutomationLiveSetting.Polite);
        Avalonia.Automation.AutomationProperties.SetLiveSetting(BannerText, Avalonia.Automation.AutomationLiveSetting.Assertive);

        DestButton.Click += (_, _) => ShowTargetMenu();
        OutputPill.Click += (_, _) => ShowOutputMenu();
        MoreButton.Click += (_, _) => ShowMoreMenu();
        CloseButton.Click += (_, _) => _vm.HideCommand.Execute(null);
        CopyButton.Click += async (_, _) => await _vm.CopyCommand.ExecuteAsync(null);
        CommitButton.Click += async (_, _) => await _vm.CommitAsync(fromKey: false);

        MicButton.AddHandler(PointerPressedEvent, OnMicPressed, RoutingStrategies.Tunnel);
        MicButton.AddHandler(PointerReleasedEvent, OnMicReleased, RoutingStrategies.Tunnel);
        MicButton.AddHandler(KeyDownEvent, OnMicKeyDown, RoutingStrategies.Tunnel);
        vm.PropertyChanged += OnViewModelChanged;
        vm.Completed += Close;
        vm.CloseRequested += Close;
        vm.RetargetRequested += ShowTargetMenu;
        vm.ToastRaised += ShowToast;
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBox.IsVisible = false;
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Deactivated += async (_, _) => await _vm.WindowDeactivatedAsync();
        Opened += (_, _) =>
        {
            Place();
            Activate();
            Panel.Opacity = 1;
            Panel.RenderTransform = TransformOperations.Identity;
            if (_vm.Lines.Count == 0 && _vm.SourceLines.Count == 0 && !_vm.HasTimeline)
                SourceBox.Focus();
            else
                MicButton.Focus();
        };
        Closed += (_, _) =>
        {
            _toastTimer.Stop();
            vm.PropertyChanged -= OnViewModelChanged;
            vm.Completed -= Close;
            vm.CloseRequested -= Close;
            vm.RetargetRequested -= ShowTargetMenu;
            vm.ToastRaised -= ShowToast;
        };

        if (vm.Animations)
        {
            Panel.Opacity = 0;
            Panel.RenderTransform = TransformOperations.Parse("translateY(10px) scale(0.98)");
            Panel.Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(180) }
            ];
        }

        Refresh();
    }

    private static void Describe(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, name);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    // ---- state -> controls ---------------------------------------------------------

    private void Refresh()
    {
        var targetName = _vm.TargetName;

        // header
        DestText.Text = _texts.Dest(_vm.HasTarget, _vm.IsTargetLost, targetName);
        DestIcon.IsVisible = _vm.IsTargetLost || _vm.HasTarget;
        DestIcon.Data = (Geometry)this.FindResource(_vm.IsTargetLost ? "IcAlert" : "IcTarget")!;
        DestButton.Classes.Set("bad", _vm.IsTargetLost);
        DestButton.Classes.Set("warn", !_vm.IsTargetLost && !_vm.HasTarget);

        var cloud = _vm.CloudTranslate || _vm.CloudClean;
        PrivacyText.Text = _texts.Privacy(_vm.CloudTranslate, _vm.CloudClean);
        PrivacyChip.Classes.Set("warn", cloud);
        PrivacyChip.Classes.Set("ok", !cloud);
        PrivacyDot.Fill = (IBrush)this.FindResource(cloud ? "Warn" : "Ok")!;
        PrivacyText.Foreground = PrivacyDot.Fill;
        Describe(PrivacyChip, _texts.PrivacyAria(_vm.CloudTranslate, _vm.CloudClean));

        OutputText.Text = _texts.OutputPill(_vm.Output, _vm.TargetLanguage);
        OutputGlobe.IsVisible = _vm.Output == ComposerOutput.Translate;

        // banner
        var banner = _vm.Banner;
        BannerBox.IsVisible = banner.Kind != BannerKind.None;
        if (banner.Kind != BannerKind.None)
            ShowBanner(banner);

        // source
        var live = _vm.IsListening || _vm.IsStartingListening || _vm.HasTimeline;
        SourceBox.IsVisible = !live;
        SourceLive.IsVisible = live;
        if (live)
            SourceLive.ItemsSource = _vm.SourceLines.Select(line => new SourceLineView(line.Time, line.Text, line.IsTail)).ToList();
        SourceLabel.Text = _texts.SourceLabel(_vm.IsListening || _vm.IsStartingListening, _vm.HasTimeline, _vm.TranscriptName);
        RouteChip.IsVisible = _vm.IsWholeRoute;
        var hasResult = _vm.Lines.Count > 0 && _vm.Output != ComposerOutput.Original;
        SourceBox.Classes.Set("dim", hasResult);
        HintPanel.IsVisible = _vm.Lines.Count == 0 && !live && string.IsNullOrWhiteSpace(_vm.SourceText);

        // result
        ResultPane.IsVisible = hasResult;
        ResultLabel.Text = _texts.ResultLabel(_vm.Output, _vm.TargetLanguage);
        if (hasResult)
            RefreshRows();
        ApplyLayout(_vm.IsWide);

        // footer
        MicButton.Classes.Set("recording", _vm.IsListening);
        MicIcon.IsVisible = !_vm.IsListening;
        MicBars.IsVisible = _vm.IsListening;
        var micName = _vm.IsListening ? _texts.MicStop : _texts.MicStart;
        Describe(MicButton, micName);
        Avalonia.Controls.ToolTip.SetTip(MicButton, micName);
        var status = _texts.Status(_vm.StatusInfo);
        StatusText.Text = status;
        StatusText.Foreground = (IBrush)this.FindResource(!_vm.IsListening && _vm.Status == ComposerStatus.Failed ? "Warn" : "Muted")!;
        CopyButton.IsEnabled = _vm.CanCopy;

        var armed = _vm.IsWriteQueued;
        CommitText.Text = _texts.CommitLabel(_vm.IsWritten, armed, _vm.HasTarget, targetName);
        CommitSpinner.IsVisible = armed;
        CommitKbd.IsVisible = !armed && !_vm.IsWritten;
        CommitKbdText.Text = ComposerTexts.CommitKeyLabel(_vm.CommitKey);
        CommitButton.IsEnabled = !_vm.IsWritten && (armed || _vm.CanWrite || _vm.Status == ComposerStatus.Working);
        Describe(CommitButton, _texts.CommitAria(armed, _vm.HasTarget));
    }

    private void ShowBanner(BannerState banner)
    {
        BannerBox.Classes.Set("info", banner.Tone == BannerTone.Info);
        BannerBox.Classes.Set("warn", banner.Tone == BannerTone.Warn);
        BannerBox.Classes.Set("bad", banner.Tone == BannerTone.Bad);
        var brush = (IBrush)this.FindResource(banner.Tone switch { BannerTone.Info => "Info", BannerTone.Warn => "Warn", _ => "Danger" })!;
        BannerIcon.Stroke = brush;
        BannerIcon.Data = (Geometry)this.FindResource(banner.Tone == BannerTone.Info ? "IcInfo" : "IcAlert")!;
        BannerText.Text = _texts.BannerMessage(banner);
        BannerText.Foreground = brush;
        BannerProgress.IsVisible = banner.Kind == BannerKind.Downloading;
        BannerProgress.Value = banner.Progress;
        Describe(BannerProgress, _texts.BannerProgressAria);

        BannerActions.Children.Clear();
        foreach (var action in banner.Actions)
        {
            var button = new Button
            {
                Classes = { "link" },
                Foreground = brush,
                Content = new TextBlock { Text = _texts.BannerActionLabel(banner, action) },
                Tag = action
            };
            button.Click += async (_, _) => await _vm.RunBannerActionCommand.ExecuteAsync(action);
            BannerActions.Children.Add(button);
        }
    }

    /// <summary>Rebuilds the sentences only when something visible changed, so hover and the caret in an edit box survive late results.</summary>
    private void RefreshRows()
    {
        var rows = _vm.Lines.Select(line => new ComposerRowView(
            _vm, line.Index, _texts.Line(line), line.State, line.Error, line.Time, _texts.RowNote(line),
            line.Warning.Length > 0, line.IsOwned, _vm.EditingIndex == line.Index,
            _vm.CanEditRows, _vm.CanEditRows && _vm.Output == ComposerOutput.Clean,
            ComposerTexts.CommitKeyLabel(_vm.CommitKey), _texts)).ToList();

        if (_rows.SequenceEqual(rows))
            return;

        _rows = rows;
        ResultList.ItemsSource = rows;
        if (_vm.EditingIndex >= 0 && _editFocusedFor != _vm.EditingIndex)
        {
            _editFocusedFor = _vm.EditingIndex;
            Dispatcher.UIThread.Post(FocusEditBox, DispatcherPriority.Loaded);
        }
        else if (_vm.EditingIndex < 0)
        {
            _editFocusedFor = -1;
        }
    }

    private void FocusEditBox()
    {
        var box = ResultList.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Name == "EditBox" && b.IsEffectivelyVisible);
        if (box is null)
            return;

        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
    }

    /// <summary>A whole document or transcript puts the source and the result side by side; otherwise they stack.</summary>
    private void ApplyLayout(bool wide)
    {
        if (wide == _wide)
            return;

        _wide = wide;
        BodyGrid.ColumnDefinitions = ColumnDefinitions.Parse(wide ? "*,*" : "*");
        BodyGrid.RowDefinitions = RowDefinitions.Parse(wide ? "Auto" : "Auto,Auto");
        Grid.SetColumn(ResultPane, wide ? 1 : 0);
        Grid.SetRow(ResultPane, wide ? 0 : 1);
        ResultPane.Margin = wide ? new Thickness(8, 6, 12, 8) : new Thickness(12, 2, 12, 8);
        SourcePane.BorderThickness = wide ? new Thickness(0, 0, 1, 0) : new Thickness(0);
        SourcePane.Classes.Set("wide", wide);
        Panel.Width = wide ? WidePanelWidth : NarrowPanelWidth;
        Width = (wide ? WidePanelWidth : NarrowPanelWidth) + ShadowMargin;
        if (IsVisible)
            Place();
    }

    // ---- menus -----------------------------------------------------------------------

    private static MenuItem Item(string header, Action onClick, bool? isChecked = null, bool radio = false, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (isChecked is { } value)
        {
            item.ToggleType = radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
            item.IsChecked = value;
        }
        item.Click += (_, _) => onClick();
        return item;
    }

    private static void Open(Control anchor, params Control[] items)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var item in items)
            flyout.Items.Add(item);
        flyout.ShowAt(anchor);
    }

    private void ShowTargetMenu()
    {
        var hasOriginal = _target is not null && !_vm.IsTargetLost;
        Open(DestButton,
            Item(_texts.TargetMenuSome(_vm.TargetName), () => _vm.CopyOnly = false, isChecked: _vm.HasTarget, radio: true, enabled: hasOriginal),
            Item(_texts.TargetMenuNone, () => _vm.CopyOnly = true, isChecked: !_vm.HasTarget, radio: true));
    }

    private void ShowOutputMenu() => Open(OutputPill,
        Item(_texts.OutputMenu(ComposerOutput.Clean, _vm.TargetLanguage), () => _vm.Output = ComposerOutput.Clean, _vm.Output == ComposerOutput.Clean, radio: true),
        Item(_texts.OutputMenu(ComposerOutput.Translate, _vm.TargetLanguage), () => _vm.Output = ComposerOutput.Translate, _vm.Output == ComposerOutput.Translate, radio: true),
        Item(_texts.OutputMenu(ComposerOutput.Original, _vm.TargetLanguage), () => _vm.Output = ComposerOutput.Original, _vm.Output == ComposerOutput.Original, radio: true),
        new Separator(),
        Item(_texts.SwapMenu(_vm.TargetLanguage), () => _vm.SwapLanguageCommand.Execute(null)));

    private void ShowMoreMenu() => Open(MoreButton,
        Item(_texts.MoreNew, () => _vm.NewDraftCommand.Execute(null)),
        Item(_texts.MoreRecent, async () => await ComposerDialogs.ShowRecentAsync(this, _texts, _recent, _vm)),
        Item(_texts.MoreImport, async () => await PickRecordingAsync(), enabled: _vm.CanReplaceDraft),
        Item(_texts.MoreExport, async () => await ComposerDialogs.ShowExportAsync(this, _texts, _vm)),
        new Separator(),
        Item(_texts.MoreWide, () => _vm.ForceWide = !_vm.ForceWide, _vm.ForceWide),
        Item(_texts.MoreWhole, () => _vm.ForceWhole = !_vm.ForceWhole, _vm.ForceWhole),
        new Separator(),
        Item(_texts.MoreSettings, () => _vm.OpenSettings()));

    private async Task PickRecordingAsync()
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

    // ---- keys and pointer ---------------------------------------------------------------

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await _vm.CancelCommand.ExecuteAsync(null);
            return;
        }

        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        var source = e.Source as Control;
        if (source is TextBox { Name: "EditBox" })
        {
            e.Handled = true;
            _vm.SaveEditCommand.Execute(null);
            return;
        }

        if (!(source == SourceBox || source == MicButton || source is Window))
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var chord = _vm.CommitKey == CommitKey.CtrlEnter ? ctrl : !ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (!chord)
            return;

        e.Handled = true;
        await _vm.CommitAsync(fromKey: true);
    }

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

    private void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: int index })
            _vm.RetryCommand.Execute(index);
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: int index })
            _vm.StartEditCommand.Execute(index);
    }

    private void OnUseOriginalClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: int index })
            _vm.UseOriginalCommand.Execute(index);
    }

    private void OnEditSaveClick(object? sender, RoutedEventArgs e) => _vm.SaveEditCommand.Execute(null);

    private void OnEditCancelClick(object? sender, RoutedEventArgs e) => _vm.CancelEditCommand.Execute(null);

    private void ShowToast(ToastKind kind)
    {
        ToastText.Text = _texts.Toast(kind);
        ToastBox.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
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
