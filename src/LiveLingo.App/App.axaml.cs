using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveLingo.App.Services.Platform.Windows;
using LiveLingo.App.ViewModels;
using LiveLingo.App.Views;

namespace LiveLingo.App;

public partial class App : Application
{
    private GlobalKeyboardHook? _keyboardHook;
    private OverlayWindow? _activeOverlay;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InstallGlobalHotkey();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InstallGlobalHotkey()
    {
        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.HotkeyPressed += OnHotkeyPressed;
        _keyboardHook.Install();
    }

    private void OnHotkeyPressed()
    {
        Dispatcher.UIThread.Post(ShowOverlay);
    }

    private void ShowOverlay()
    {
        if (_activeOverlay is { IsVisible: true })
        {
            _activeOverlay.Activate();
            return;
        }

        var targetWindow = WindowTracker.GetForegroundWindowInfo();
        if (targetWindow is null)
            return;

        var currentPid = Environment.ProcessId;
        NativeMethods.GetWindowThreadProcessId(targetWindow.Handle, out var targetPid);
        if (targetPid == (uint)currentPid)
            return;

        // Allow our process to set foreground window freely
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        var vm = new OverlayViewModel(targetWindow);
        _activeOverlay = new OverlayWindow(vm);

        PositionOverlay(_activeOverlay, targetWindow);

        _activeOverlay.Closed += (_, _) => _activeOverlay = null;
        _activeOverlay.Show();
        _activeOverlay.Activate();
    }

    private static void PositionOverlay(Window overlay, TargetWindowInfo target)
    {
        var overlayWidth = (int)overlay.Width;
        var overlayHeight = (int)overlay.Height;

        var x = target.Left + (target.Width - overlayWidth) / 2;
        var y = target.Top + target.Height - overlayHeight - 80;

        if (y < target.Top)
            y = target.Top + 20;

        overlay.Position = new PixelPoint(x, y);
    }
}
