using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveLingo.App.Services.Platform.Windows;
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
        vm.RequestClose += () => Dispatcher.UIThread.Post(Close);

        // Tunnel routing intercepts keys BEFORE the TextBox consumes them
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);

        var dragHandle = this.FindControl<Border>("DragHandle");
        if (dragHandle is not null)
        {
            dragHandle.PointerPressed += OnDragHandlePointerPressed;
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
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
        var textToSend = vm.TranslatedText;
        var targetHandle = vm.TargetWindowHandle;
        var inputChild = vm.TargetInputChild;
        var autoSend = vm.AutoSend;

        Hide();

        DispatcherTimer.RunOnce(() =>
        {
            TextInjector.InjectText(targetHandle, inputChild, textToSend, autoSend);
            Close();
        }, TimeSpan.FromMilliseconds(200));
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void FocusSourceInput()
    {
        var input = this.FindControl<TextBox>("SourceInput");
        if (input is null) return;

        input.Focus();
        input.CaretIndex = input.Text?.Length ?? 0;
    }
}
