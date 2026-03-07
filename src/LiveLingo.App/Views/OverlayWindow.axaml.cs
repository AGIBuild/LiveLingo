using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LiveLingo.App.Platform.Windows;
using LiveLingo.App.ViewModels;

namespace LiveLingo.App.Views;

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

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => FadeOutAndClose();

    private void FocusSourceInput()
    {
        var input = this.FindControl<TextBox>("SourceInput");
        if (input is null) return;

        input.Focus();
        input.CaretIndex = input.Text?.Length ?? 0;
    }
}
