using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveLingo.App.Platform;
using LiveLingo.App.Platform.Windows;
using LiveLingo.App.Platform.macOS;
using LiveLingo.App.Services.Configuration;
using LiveLingo.App.Services.Localization;
using LiveLingo.App.Services.Update;
using LiveLingo.App.ViewModels;
using LiveLingo.App.Views;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.ComponentModel;
using System.Reflection;

namespace LiveLingo.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private OverlayWindow? _activeOverlay;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private string? _currentHotkeyId;
    private string? _currentHotkeyGesture;
    private Timer? _updateCheckTimer;
    private IUpdateService? _updateService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var settingsService = new JsonSettingsService();
        await settingsService.LoadAsync();
        var userSettings = settingsService.Current;

        var logDirectory = Path.Combine(GetUserDataDirectory(), "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "livelingo-.log");

        var minimumLevel = ParseSerilogLevel(userSettings.Advanced.LogLevel);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(logPath,
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Write(minimumLevel, "Serilog initialized. Log file pattern: {LogPath}", logPath);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
        services.AddLiveLingoCore(opts =>
        {
            if (!string.IsNullOrEmpty(userSettings.Advanced.ModelStoragePath))
                opts.ModelStoragePath = userSettings.Advanced.ModelStoragePath;
            opts.DefaultTargetLanguage = userSettings.Translation.DefaultTargetLanguage;
            opts.InferenceThreads = userSettings.Advanced.InferenceThreads;
        });
        services.AddSingleton<ISettingsService>(settingsService);

        services.AddSingleton<ILocalizationService>(sp =>
        {
            var loc = new LocalizationService(sp.GetService<ILogger<LocalizationService>>());
            loc.SetCulture(userSettings.UI.Language);
            return loc;
        });

        if (OperatingSystem.IsWindows())
            services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IPlatformServices, MacPlatformServices>();

        _serviceProvider = services.BuildServiceProvider();

        var updateUrl = settingsService.Current.Update.UpdateUrl;
        if (!string.IsNullOrEmpty(updateUrl))
        {
            var updateLogger = _serviceProvider.GetRequiredService<ILogger<VelopackUpdateService>>();
            _updateService = new VelopackUpdateService(updateUrl, updateLogger);
            StartUpdateChecks(_updateService, settingsService.Current.Update.CheckIntervalHours);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!settingsService.SettingsFileExists())
            {
                var modelManager = _serviceProvider.GetRequiredService<IModelManager>();
                await ShowSetupWizardAsync(settingsService, modelManager, startStep: 0);
            }

            var platform = _serviceProvider.GetService<IPlatformServices>();
            SetupTrayIcon(desktop, platform, settingsService);
            if (platform is not null)
            {
                RegisterHotkey(platform, settingsService.Current);
                platform.Hotkey.HotkeyTriggered += args =>
                    Dispatcher.UIThread.Post(() => ShowOverlay(platform, settingsService));

                settingsService.SettingsChanged += newSettings =>
                    Dispatcher.UIThread.Post(() => ApplyRuntimeSettings(platform, newSettings));
            }
            _ = RunStartupHealthChecksAsync(settingsService);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        IPlatformServices? platform,
        ISettingsService settingsService)
    {
        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "LiveLingo"
        };
        BuildTrayMenu(desktop, platform, settingsService);
    }

    private void BuildTrayMenu(
        IClassicDesktopStyleApplicationLifetime desktop,
        IPlatformServices? platform,
        ISettingsService settingsService)
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();

        var translateItem = new NativeMenuItem(loc.T("tray.openTranslator"))
        {
            IsEnabled = platform is not null
        };
        translateItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (platform is null) return;
            ShowOverlay(platform, settingsService);
        });

        var settingsItem = new NativeMenuItem(loc.T("tray.settings"));
        settingsItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowSettings);

        var checkUpdateItem = new NativeMenuItem(loc.T("tray.checkUpdates"))
        {
            IsEnabled = _updateService is not null
        };
        checkUpdateItem.Click += (_, _) => Dispatcher.UIThread.Post(async () => await CheckForUpdatesFromMenuAsync());

        var aboutItem = new NativeMenuItem(loc.T("tray.about"));
        aboutItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowAboutDialog);

        var quitItem = new NativeMenuItem(loc.T("tray.quit"));
        quitItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Log.Information("Application shutting down from tray menu.");
            Log.CloseAndFlush();
            _trayIcon?.Dispose();
            _trayIcon = null;
            desktop.Shutdown();
        });

        var menu = new NativeMenu
        {
            Items =
            {
                translateItem,
                settingsItem,
                checkUpdateItem,
                aboutItem,
                new NativeMenuItemSeparator(),
                quitItem
            }
        };

        if (_trayIcon is not null)
            _trayIcon.Menu = menu;
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
        var modelManager = _serviceProvider!.GetRequiredService<IModelManager>();
        var engine = _serviceProvider!.GetRequiredService<ITranslationEngine>();
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        var vm = new SettingsViewModel(settingsService, modelManager, engine);
        PropertyChangedEventHandler? handler = (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.OverlayOpacity) &&
                _activeOverlay is { IsVisible: true })
            {
                _activeOverlay.SetBackgroundOpacity(vm.OverlayOpacity);
            }
        };
        vm.PropertyChanged += handler;
        _settingsWindow = new SettingsWindow(vm, loc);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.PropertyChanged -= handler;
            _settingsWindow = null;
        };
        _settingsWindow.Show();
    }

    private void ShowAboutDialog()
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        var assembly = typeof(App).Assembly;
        var productName = assembly.GetName().Name ?? "LiveLingo";
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var body = loc.T("dialog.about.body", productName, version);
        ShowInfoDialog(loc.T("dialog.about.title"), body, primaryText: loc.T("dialog.ok"));
    }

    private async Task CheckForUpdatesFromMenuAsync()
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        try
        {
            if (_updateService is null)
            {
                ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.notConfigured"),
                    primaryText: loc.T("dialog.ok"));
                return;
            }

            var hasUpdate = await _updateService.CheckForUpdateAsync();
            if (!hasUpdate)
            {
                ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.latest"),
                    primaryText: loc.T("dialog.ok"));
                return;
            }

            var targetVersion = _updateService.AvailableVersion ?? "unknown";
            ShowInfoDialog(
                loc.T("dialog.update.available.title"),
                loc.T("dialog.update.available.body", targetVersion),
                primaryText: loc.T("dialog.update.available.download"),
                primaryAction: async () =>
                {
                    await _updateService.DownloadAndApplyAsync();
                },
                secondaryText: loc.T("dialog.update.available.later"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual update check failed from tray menu.");
            ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.failed", ex.Message),
                primaryText: loc.T("dialog.ok"));
        }
    }

    private static void ShowInfoDialog(
        string title,
        string message,
        string primaryText = "OK",
        Func<Task>? primaryAction = null,
        string? secondaryText = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        var content = new DockPanel { Margin = new Thickness(16) };
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(text, Dock.Top);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = new Button { Content = secondaryText, Padding = new Thickness(12, 6) };
            secondary.Click += (_, _) => dialog.Close();
            buttons.Children.Add(secondary);
        }

        var primary = new Button { Content = primaryText, Padding = new Thickness(12, 6) };
        primary.Click += async (_, _) =>
        {
            if (primaryAction is not null)
                await primaryAction();
            dialog.Close();
        };
        buttons.Children.Add(primary);

        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(text);
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.Show();
    }

    private void RegisterHotkey(IPlatformServices platform, UserSettings settings)
    {
        var binding = HotkeyParser.Parse("overlay", settings.Hotkeys.OverlayToggle);
        platform.Hotkey.Register(binding);
        _currentHotkeyId = binding.Id;
        _currentHotkeyGesture = settings.Hotkeys.OverlayToggle;
    }

    private void ReloadHotkey(IPlatformServices platform, UserSettings settings)
    {
        if (_currentHotkeyId is not null)
            platform.Hotkey.Unregister(_currentHotkeyId);

        RegisterHotkey(platform, settings);
    }

    private void ApplyRuntimeSettings(IPlatformServices platform, UserSettings settings)
    {
        if (!string.Equals(_currentHotkeyGesture, settings.Hotkeys.OverlayToggle, StringComparison.OrdinalIgnoreCase))
            ReloadHotkey(platform, settings);

        if (_activeOverlay is { IsVisible: true })
            _activeOverlay.SetBackgroundOpacity(settings.UI.OverlayOpacity);

        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        if (!string.Equals(loc.CurrentCulture.Name, settings.UI.Language, StringComparison.OrdinalIgnoreCase))
        {
            loc.SetCulture(settings.UI.Language);
            var settingsService = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                BuildTrayMenu(desktop, platform, settingsService);
        }
    }

    private void ShowOverlay(IPlatformServices platform, ISettingsService settingsService)
    {
        if (_activeOverlay is { IsVisible: true })
        {
            _activeOverlay.Activate();
            return;
        }

        var modelManager = _serviceProvider!.GetRequiredService<IModelManager>();
        if (!IsRequiredModelReady(modelManager))
        {
            _ = ShowSetupWizardAsync(settingsService, modelManager, startStep: 2);
            return;
        }

        var target = platform.WindowTracker.GetForegroundWindowInfo();
        if (target is null) return;

        var currentPid = Environment.ProcessId;
        NativeMethods.GetWindowThreadProcessId(target.Handle, out var targetPid);
        if (targetPid == (uint)currentPid) return;

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        var pipeline = _serviceProvider!.GetRequiredService<ITranslationPipeline>();
        var engine = _serviceProvider!.GetRequiredService<ITranslationEngine>();
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        var overlayLogger = _serviceProvider!.GetRequiredService<ILogger<OverlayViewModel>>();
        var clipboard = platform.Clipboard;
        var vm = new OverlayViewModel(target, pipeline, platform.TextInjector, engine, settingsService.Current, clipboard, loc, settingsService, overlayLogger);
        vm.RequestOpenSettings += () => Dispatcher.UIThread.Post(ShowSettings);
        _activeOverlay = new OverlayWindow(vm);

        PositionOverlay(_activeOverlay, target);

        var uiSettings = settingsService.Current.UI;
        _activeOverlay.SetBackgroundOpacity(uiSettings.OverlayOpacity);
        _activeOverlay.Closed += (_, _) =>
        {
            vm.PersistIfChanged();
            _activeOverlay = null;
        };
        _activeOverlay.Show();
        _activeOverlay.Activate();

        DispatcherTimer.RunOnce(() =>
        {
            if (_activeOverlay is not null)
                ClampToScreen(_activeOverlay);
        }, TimeSpan.FromMilliseconds(50));
    }

    private static bool IsRequiredModelReady(IModelManager modelManager)
    {
        var installed = modelManager.ListInstalled();
        return ModelRegistry.RequiredModels.All(
            req => installed.Any(m => m.Id == req.Id));
    }

    private SetupWizardWindow? _wizardWindow;

    private async Task ShowSetupWizardAsync(ISettingsService settingsService, IModelManager modelManager, int startStep)
    {
        if (_wizardWindow is { IsVisible: true })
        {
            _wizardWindow.Activate();
            return;
        }

        var wizardVm = new SetupWizardViewModel(settingsService, modelManager, startStep);
        _wizardWindow = new SetupWizardWindow(wizardVm);
        var done = new TaskCompletionSource();
        _wizardWindow.Closed += (_, _) =>
        {
            _wizardWindow = null;
            done.SetResult();
        };
        _wizardWindow.Show();
        await done.Task;
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
        var estimatedHeight = 260;

        var threadId = NativeMethods.GetWindowThreadProcessId(target.Handle, out _);
        var gui = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };

        if (NativeMethods.GetGUIThreadInfo(threadId, ref gui) && gui.hwndCaret != IntPtr.Zero)
        {
            var pt = new NativeMethods.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
            NativeMethods.ClientToScreen(gui.hwndCaret, ref pt);

            var caretHeight = gui.rcCaret.Bottom - gui.rcCaret.Top;
            var x = pt.X - overlayWidth / 2;
            var y = pt.Y - estimatedHeight - 8;
            if (y < 0) y = pt.Y + caretHeight + 8;

            overlay.Position = new PixelPoint(x, y);
            return;
        }

        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var x = cursor.X - overlayWidth / 2;
            var y = cursor.Y - estimatedHeight - 12;
            if (y < 0) y = cursor.Y + 20;

            overlay.Position = new PixelPoint(x, y);
            return;
        }

        var fx = target.Left + (target.Width - overlayWidth) / 2;
        var fy = target.Top + target.Height - estimatedHeight - 80;
        if (fy < target.Top) fy = target.Top + 20;
        overlay.Position = new PixelPoint(fx, fy);
    }

    private static void ClampToScreen(Window overlay)
    {
        var screen = overlay.Screens.ScreenFromWindow(overlay);
        if (screen is null) return;

        var workArea = screen.WorkingArea;
        var pos = overlay.Position;
        var w = (int)overlay.Bounds.Width;
        var h = (int)overlay.Bounds.Height;
        if (w == 0) w = (int)overlay.Width;
        if (h == 0) h = 260;

        var x = pos.X;
        var y = pos.Y;

        if (x < workArea.X) x = workArea.X;
        if (y < workArea.Y) y = workArea.Y;
        if (x + w > workArea.Right) x = workArea.Right - w;
        if (y + h > workArea.Bottom) y = workArea.Bottom - h;

        if (x != pos.X || y != pos.Y)
            overlay.Position = new PixelPoint(x, y);
    }

    private async Task RunStartupHealthChecksAsync(ISettingsService settingsService)
    {
        await Task.Delay(2000);

        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        var issues = new List<string>();
        var modelManager = _serviceProvider!.GetRequiredService<IModelManager>();
        if (!IsRequiredModelReady(modelManager))
            issues.Add(loc.T("toast.modelNotDownloaded"));

        if (issues.Count == 0) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var message = string.Join("; ", issues);
            var toast = new NotificationToast(message, TimeSpan.FromSeconds(15), loc.T("toast.configure"));
            toast.ConfigureRequested += () =>
            {
                _ = ShowSetupWizardAsync(settingsService, modelManager,
                    startStep: issues.Contains("AI model not downloaded") ? 2 : 0);
            };
            toast.Show();
        });
    }

    private static Serilog.Events.LogEventLevel ParseSerilogLevel(string level) => level switch
    {
        "Verbose" or "Trace" => Serilog.Events.LogEventLevel.Verbose,
        "Debug" => Serilog.Events.LogEventLevel.Debug,
        "Warning" => Serilog.Events.LogEventLevel.Warning,
        "Error" => Serilog.Events.LogEventLevel.Error,
        _ => Serilog.Events.LogEventLevel.Information,
    };

    private static string GetUserDataDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "LiveLingo");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveLingo");
    }
}
