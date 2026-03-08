using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveLingo.Desktop.Platform.Windows;
using LiveLingo.Desktop.ViewModels;

namespace LiveLingo.Desktop.Views;

public partial class OverlayWindow : Window
{
    private bool _isSending;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public OverlayWindow(OverlayViewModel vm) : this()
    {
        DataContext = vm;
        vm.RequestClose += () => Dispatcher.UIThread.Post(() => FadeOutAndClose());

        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);

        var dragHandle = this.FindControl<Border>("DragHandle");
        if (dragHandle is not null)
            dragHandle.PointerPressed += OnDragHandlePointerPressed;

        var langLink = this.FindControl<Avalonia.Controls.TextBlock>("TargetLangLink");
        if (langLink is not null)
            langLink.PointerPressed += (_, _) => vm.ToggleLanguagePickerCommand.Execute(null);

        AttachResizeGrips();
    }

    private void AttachResizeGrips()
    {
        (string name, WindowEdge edge)[] grips =
        [
            ("ResizeN",  WindowEdge.North),
            ("ResizeS",  WindowEdge.South),
            ("ResizeW",  WindowEdge.West),
            ("ResizeE",  WindowEdge.East),
            ("ResizeNW", WindowEdge.NorthWest),
            ("ResizeNE", WindowEdge.NorthEast),
            ("ResizeSW", WindowEdge.SouthWest),
            ("ResizeSE", WindowEdge.SouthEast),
        ];

        foreach (var (name, edge) in grips)
        {
            var el = this.FindControl<Control>(name);
            if (el is null) continue;
            var e2 = edge;
            el.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    BeginResizeDrag(e2, args);
            };
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OverlayViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CancelCommand.Execute(null);
        }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            e.Handled = true;
            if (!_isSending)
                PerformSend(vm);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Activate();
        Topmost = true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ForceActivateWindows();
        }

        var rootPanel = this.FindControl<Panel>("RootPanel");
        if (rootPanel is not null)
        {
            DispatcherTimer.RunOnce(() => rootPanel.Opacity = 1, TimeSpan.FromMilliseconds(30));
        }

        DispatcherTimer.RunOnce(FocusSourceInput, TimeSpan.FromMilliseconds(150));
    }

    private void ForceActivateWindows()
    {
        var handle = TryGetPlatformHandle();
        if (handle is null) return;

        var hwnd = handle.Handle;
        var currentThreadId = NativeMethods.GetCurrentThreadId();
        var foregroundHwnd = NativeMethods.GetForegroundWindow();
        var foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out _);

        if (currentThreadId != foregroundThreadId)
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);

        if (currentThreadId != foregroundThreadId)
            NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
    }

    private void PerformSend(OverlayViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.TranslatedText))
            return;

        _isSending = true;
        Hide();

        DispatcherTimer.RunOnce(async () =>
        {
            await vm.InjectAsync();
            Close();
        }, TimeSpan.FromMilliseconds(200));
    }

    private void FadeOutAndClose()
    {
        var rootPanel = this.FindControl<Panel>("RootPanel");
        if (rootPanel is null)
        {
            Close();
            return;
        }

        rootPanel.Opacity = 0;
        DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(160));
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void SetBackgroundOpacity(double opacity)
    {
        var frameAlpha = (byte)(opacity * 255);
        var panelAlpha = (byte)(Math.Max(opacity, 0.55) * 255);

        // t: 0 at min opacity (0.1), 1 at max opacity (1.0)
        var t = Math.Clamp((opacity - 0.1) / 0.9, 0, 1);

        Resources["OvFrameBrush"] = MakeBrush(frameAlpha, 0x1C, 0x1C, 0x1E);
        Resources["OvPanelBrush"] = MakeBrush(panelAlpha, 0x1C, 0x1C, 0x1E);

        Resources["OvFgPrimaryBrush"]   = new SolidColorBrush(Lerp(0xFF, 0xFF, 0xFF, 0xE5, 0xE5, 0xE7, t));
        Resources["OvFgSecondaryBrush"] = new SolidColorBrush(Lerp(0xF0, 0xF0, 0xF2, 0xC7, 0xC7, 0xCC, t));
        Resources["OvFgTertiaryBrush"]  = new SolidColorBrush(Lerp(0xE0, 0xE0, 0xE4, 0xAE, 0xAE, 0xB2, t));
        Resources["OvFgMutedBrush"]     = new SolidColorBrush(Lerp(0xD5, 0xD5, 0xD9, 0xA1, 0xA1, 0xA6, t));
    }

    private static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b)
        => new(Color.FromArgb(a, r, g, b));

    private static Color Lerp(
        byte r0, byte g0, byte b0,
        byte r1, byte g1, byte b1,
        double t)
    {
        return Color.FromRgb(
            (byte)(r0 + (r1 - r0) * t),
            (byte)(g0 + (g1 - g0) * t),
            (byte)(b0 + (b1 - b0) * t));
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => FadeOutAndClose();

    private void SendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OverlayViewModel vm && !_isSending)
            PerformSend(vm);
    }

    private void FocusSourceInput()
    {
        var input = this.FindControl<TextBox>("SourceInput");
        if (input is null) return;

        input.Focus();
        input.CaretIndex = input.Text?.Length ?? 0;
    }
}
