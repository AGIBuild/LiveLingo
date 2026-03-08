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
    private bool _isResizingFromCorner;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private readonly CancellationTokenSource _lifetimeCts = new();

    public OverlayWindow()
    {
        InitializeComponent();
    }

    public OverlayWindow(OverlayViewModel vm) : this()
    {
        DataContext = vm;
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);

        var dragHandle = this.FindControl<Border>("DragHandle");
        if (dragHandle is not null)
            dragHandle.PointerPressed += OnDragHandlePointerPressed;

        var langLink = this.FindControl<Control>("TargetLangLink");
        if (langLink is not null)
            langLink.PointerPressed += (_, _) => vm.ToggleLanguagePickerCommand.Execute(null);

        AttachResizeHandle();
    }

    private void AttachResizeHandle()
    {
        var handle = this.FindControl<Control>("ResizeSE");
        if (handle is null) return;

        handle.PointerPressed += OnResizeHandlePressed;
        handle.PointerMoved += OnResizeHandleMoved;
        handle.PointerReleased += OnResizeHandleReleased;
        handle.PointerCaptureLost += (_, _) => EndResizeInteraction();
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
            if (!vm.IsSending)
                _ = vm.SendAsync(_lifetimeCts.Token);
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

    public void PrepareForShutdown()
    {
        _lifetimeCts.Cancel();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PrepareForShutdown();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifetimeCts.Dispose();
        base.OnClosed(e);
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

    public void RequestCloseAnimated() => FadeOutAndClose();

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void ApplyAutoSizingDefaults()
    {
        SizeToContent = SizeToContent.Height;
        MaxHeight = 600;
        Width = Math.Max(MinWidth, 600);
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
        if (DataContext is OverlayViewModel vm && !vm.IsSending)
            _ = vm.SendAsync(_lifetimeCts.Token);
    }

    private void FocusSourceInput()
    {
        var input = this.FindControl<TextBox>("SourceInput");
        if (input is null) return;

        input.Focus();
        input.CaretIndex = input.Text?.Length ?? 0;
    }

    private void OnResizeHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sender is not Control handle)
            return;

        if (SizeToContent != SizeToContent.Manual)
        {
            SizeToContent = SizeToContent.Manual;
            MaxHeight = 900;
            var currentBoundsHeight = Bounds.Height > 0
                ? Bounds.Height
                : (double.IsNaN(Height) ? MinHeight : Height);
            var currentBoundsWidth = Bounds.Width > 0
                ? Bounds.Width
                : (double.IsNaN(Width) ? MinWidth : Width);
            Width = Math.Max(MinWidth, currentBoundsWidth);
            Height = Math.Clamp(currentBoundsHeight, MinHeight, MaxHeight);
        }

        _isResizingFromCorner = true;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnResizeHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingFromCorner)
            return;

        var current = e.GetPosition(this);
        var deltaX = current.X - _resizeStartPoint.X;
        var deltaY = current.Y - _resizeStartPoint.Y;
        Width = Math.Max(MinWidth, _resizeStartWidth + deltaX);
        Height = Math.Clamp(_resizeStartHeight + deltaY, MinHeight, MaxHeight);
        e.Handled = true;
    }

    private void OnResizeHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);
        EndResizeInteraction();
        e.Handled = true;
    }

    private void EndResizeInteraction()
    {
        _isResizingFromCorner = false;
    }
}
