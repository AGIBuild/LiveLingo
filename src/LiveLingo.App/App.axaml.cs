using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveLingo.App.Platform;
using LiveLingo.App.Platform.Windows;
using LiveLingo.App.Platform.macOS;
using LiveLingo.App.Services.Configuration;
using LiveLingo.App.Services.Update;
using LiveLingo.App.ViewModels;
using LiveLingo.App.Views;
using LiveLingo.Core;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiveLingo.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private OverlayWindow? _activeOverlay;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private string? _currentHotkeyId;
    private Timer? _updateCheckTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiveLingoCore();
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        if (OperatingSystem.IsWindows())
            services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IPlatformServices, MacPlatformServices>();

        _serviceProvider = services.BuildServiceProvider();

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();

        var updateUrl = settingsService.Current.Update.UpdateUrl;
        if (!string.IsNullOrEmpty(updateUrl))
        {
            var updateLogger = _serviceProvider.GetRequiredService<ILogger<VelopackUpdateService>>();
            var updateService = new VelopackUpdateService(updateUrl, updateLogger);
            StartUpdateChecks(updateService, settingsService.Current.Update.CheckIntervalHours);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!settingsService.SettingsFileExists())
            {
                var modelManager = _serviceProvider.GetRequiredService<IModelManager>();
                var wizardVm = new SetupWizardViewModel(settingsService, modelManager);
                var wizard = new SetupWizardWindow(wizardVm);
                await wizard.ShowDialog(new Window());
            }

            var mainWindow = new MainWindow();
            mainWindow.Icon = LoadMainIcon();
            mainWindow.SetHotkeyDisplay(settingsService.Current.Hotkeys.OverlayToggle);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon(desktop);

            var platform = _serviceProvider.GetService<IPlatformServices>();
            if (platform is not null)
            {
                RegisterHotkey(platform, settingsService.Current);
                platform.Hotkey.HotkeyTriggered += args =>
                    Dispatcher.UIThread.Post(() => ShowOverlay(platform, settingsService));

                settingsService.SettingsChanged += newSettings =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        ReloadHotkey(platform, newSettings);
                        mainWindow.SetHotkeyDisplay(newSettings.Hotkeys.OverlayToggle);
                    });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowSettings);

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            desktop.Shutdown();
        });

        var menu = new NativeMenu
        {
            Items =
            {
                settingsItem,
                new NativeMenuItemSeparator(),
                quitItem
            }
        };

        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "LiveLingo",
            Menu = menu
        };
    }

    private static WindowIcon LoadMainIcon()
    {
        var iconPath = OperatingSystem.IsWindows()
            ? "avares://LiveLingo.App/Assets/app-icon.ico"
            : "avares://LiveLingo.App/Assets/app-icon-512.png";
        return new WindowIcon(AssetLoader.Open(new Uri(iconPath)));
    }

    private static WindowIcon LoadTrayIcon()
    {
        var iconPath = OperatingSystem.IsWindows()
            ? "avares://LiveLingo.App/Assets/tray-icon.ico"
            : "avares://LiveLingo.App/Assets/tray-icon-mac.png";
        return new WindowIcon(AssetLoader.Open(new Uri(iconPath)));
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsService = _serviceProvider!.GetRequiredService<ISettingsService>();
        var vm = new SettingsViewModel(settingsService);
        _settingsWindow = new SettingsWindow(vm);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void RegisterHotkey(IPlatformServices platform, UserSettings settings)
    {
        var binding = HotkeyParser.Parse("overlay", settings.Hotkeys.OverlayToggle);
        platform.Hotkey.Register(binding);
        _currentHotkeyId = binding.Id;
    }

    private void ReloadHotkey(IPlatformServices platform, UserSettings settings)
    {
        if (_currentHotkeyId is not null)
            platform.Hotkey.Unregister(_currentHotkeyId);

        RegisterHotkey(platform, settings);
    }

    private void ShowOverlay(IPlatformServices platform, ISettingsService settingsService)
    {
        if (_activeOverlay is { IsVisible: true })
        {
            _activeOverlay.Activate();
            return;
        }

        var target = platform.WindowTracker.GetForegroundWindowInfo();
        if (target is null) return;

        var currentPid = Environment.ProcessId;
        NativeMethods.GetWindowThreadProcessId(target.Handle, out var targetPid);
        if (targetPid == (uint)currentPid) return;

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        var pipeline = _serviceProvider!.GetRequiredService<ITranslationPipeline>();
        var targetLang = settingsService.Current.Translation.DefaultTargetLanguage;
        var vm = new OverlayViewModel(target, pipeline, platform.TextInjector, targetLang);
        _activeOverlay = new OverlayWindow(vm);

        var uiSettings = settingsService.Current.UI;
        if (uiSettings.LastOverlayPosition is { } savedPos)
            _activeOverlay.Position = new PixelPoint(savedPos.X, savedPos.Y);
        else
            PositionOverlay(_activeOverlay, target);

        _activeOverlay.Opacity = uiSettings.OverlayOpacity;
        _activeOverlay.Closed += (_, _) => _activeOverlay = null;
        _activeOverlay.Show();
        _activeOverlay.Activate();
    }

    private void StartUpdateChecks(IUpdateService updateService, int intervalHours)
    {
        _ = Task.Run(async () =>
        {
            await updateService.CheckForUpdateAsync();
        });

        var interval = TimeSpan.FromHours(Math.Max(1, intervalHours));
        _updateCheckTimer = new Timer(_ =>
        {
            _ = Task.Run(async () =>
            {
                await updateService.CheckForUpdateAsync();
            });
        }, null, interval, interval);
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
