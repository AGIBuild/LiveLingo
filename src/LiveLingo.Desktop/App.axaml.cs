using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Platform.Windows;
using LiveLingo.Desktop.Platform.macOS;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Update;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.Views;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.ComponentModel;
using System.Reflection;

namespace LiveLingo.Desktop;

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
    private bool _accessibilityWarned;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (OperatingSystem.IsMacOS())
            SetMacActivationPolicyAccessory();

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
                {
                    Log.Information("HotkeyTriggered event received: id={Id}", args.HotkeyId);
                    Dispatcher.UIThread.Post(() => ShowOverlay(platform, settingsService));
                };

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

        var checkUpdateItem = new NativeMenuItem(loc.T("tray.checkUpdates"));
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

        if (_trayIcon is null) return;

        var menu = _trayIcon.Menu ?? new NativeMenu();
        menu.Items.Clear();
        menu.Items.Add(translateItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(checkUpdateItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        if (_trayIcon.Menu is null)
            _trayIcon.Menu = menu;
    }

    private static WindowIcon LoadTrayIcon()
    {
        var iconPath = OperatingSystem.IsWindows()
            ? "avares://LiveLingo.Desktop/Assets/tray-icon.ico"
            : "avares://LiveLingo.Desktop/Assets/tray-icon-mac.png";
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
            if (e.PropertyName == nameof(SettingsViewModel.OverlayOpacity))
            {
                if (_activeOverlay is { IsVisible: true })
                    _activeOverlay.SetBackgroundOpacity(vm.OverlayOpacity);
                _settingsWindow?.SetBackgroundOpacity(vm.OverlayOpacity);
            }
        };
        vm.PropertyChanged += handler;
        _settingsWindow = new SettingsWindow(vm, loc);
        _settingsWindow.SetBackgroundOpacity(vm.OverlayOpacity);
        _settingsWindow.Closed += (_, _) =>
        {
            vm.PropertyChanged -= handler;
            _settingsWindow = null;
        };
        _settingsWindow.Show();
    }

    private void ShowAboutDialog()
    {
        var assembly = typeof(App).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var bgSurface = new SolidColorBrush(Color.Parse("#1C1C1E"));
        var bgElevated = new SolidColorBrush(Color.Parse("#2A2A2C"));
        var borderSubtle = new SolidColorBrush(Color.Parse("#3A3A3C"));
        var fgPrimary = new SolidColorBrush(Color.Parse("#E5E5E7"));
        var fgSecondary = new SolidColorBrush(Color.Parse("#C7C7CC"));
        var fgMuted = new SolidColorBrush(Color.Parse("#8E8E93"));
        var accent = new SolidColorBrush(Color.Parse("#0A84FF"));

        var dialog = new Window
        {
            Title = "About LiveLingo",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = bgSurface,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowDecorations = WindowDecorations.Full
        };

        var root = new StackPanel
        {
            Margin = new Thickness(32, 28),
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var iconBorder = new Border
        {
            Width = 72, Height = 72,
            CornerRadius = new CornerRadius(16),
            Background = bgElevated,
            BorderBrush = borderSubtle,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = "🌐",
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        root.Children.Add(iconBorder);

        root.Children.Add(new TextBlock
        {
            Text = "LiveLingo",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = fgPrimary,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        root.Children.Add(new TextBlock
        {
            Text = $"Version {version}",
            FontSize = 12,
            Foreground = fgMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 10)
        });

        var separator = new Border
        {
            Height = 1,
            Background = borderSubtle,
            Margin = new Thickness(0, 4)
        };
        root.Children.Add(separator);

        root.Children.Add(new TextBlock
        {
            Text = "Local AI-powered translation assistant.\nReal-time overlay for any application.",
            FontSize = 13,
            Foreground = fgSecondary,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            LineHeight = 20,
            Margin = new Thickness(0, 8, 0, 4)
        });

        root.Children.Add(new TextBlock
        {
            Text = $"© {DateTime.Now.Year} LiveLingo Contributors",
            FontSize = 11,
            Foreground = fgMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 14)
        });

        var okBtn = new Button
        {
            Content = "OK",
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(28, 7),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        okBtn.Click += (_, _) => dialog.Close();
        root.Children.Add(okBtn);

        dialog.Content = root;
        dialog.Show();
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
        string? secondaryText = null,
        Func<Task>? secondaryAction = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
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
            secondary.Click += async (_, _) =>
            {
                if (secondaryAction is not null)
                    await secondaryAction();
                else
                    dialog.Close();
            };
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

    private bool RegisterHotkey(IPlatformServices platform, UserSettings settings)
    {
        var gesture = settings.Hotkeys.OverlayToggle;
        Log.Information("Registering hotkey: {Gesture}", gesture);

        if (OperatingSystem.IsMacOS())
        {
            var ax = Platform.macOS.AccessibilityPermission.IsGranted();
            var im = Platform.macOS.AccessibilityPermission.IsInputMonitoringGranted();
            Log.Information("macOS permissions — Accessibility: {AX}, InputMonitoring: {IM}", ax, im);
        }

        try
        {
            var binding = HotkeyParser.Parse("overlay", gesture);
            platform.Hotkey.Register(binding);
            _currentHotkeyId = binding.Id;
            _currentHotkeyGesture = gesture;
            _accessibilityWarned = false;
            Log.Information("Hotkey registered successfully: {Gesture} (id={Id})", gesture, binding.Id);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register hotkey: {Gesture}", gesture);

            if (!_accessibilityWarned && OperatingSystem.IsMacOS())
            {
                _accessibilityWarned = true;
                PromptAccessibilityPermission();
            }

            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void PromptAccessibilityPermission()
    {
        ShowInfoDialog(
            "System Permission Required",
            "LiveLingo needs two macOS permissions to register global hotkeys:\n\n" +
            "1. Privacy & Security → Accessibility\n" +
            "2. Privacy & Security → Input Monitoring\n\n" +
            "Add and enable the terminal app you're using (e.g. Terminal, Cursor, iTerm) in BOTH lists.\n" +
            "After granting, restart the terminal and the app.",
            primaryText: "Open Accessibility",
            primaryAction: () =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
                    UseShellExecute = false
                });
                return Task.CompletedTask;
            },
            secondaryText: "Open Input Monitoring",
            secondaryAction: () =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent",
                    UseShellExecute = false
                });
                return Task.CompletedTask;
            });
    }

    private bool ReloadHotkey(IPlatformServices platform, UserSettings settings)
    {
        if (_currentHotkeyId is not null)
            platform.Hotkey.Unregister(_currentHotkeyId);

        return RegisterHotkey(platform, settings);
    }

    private void ApplyRuntimeSettings(IPlatformServices platform, UserSettings settings)
    {
        try
        {
            if (!string.Equals(_currentHotkeyGesture, settings.Hotkeys.OverlayToggle, StringComparison.OrdinalIgnoreCase))
            {
                var ok = ReloadHotkey(platform, settings);
                Dispatcher.UIThread.Post(() =>
                {
                    if (ok)
                        ShowNotification($"Hotkey updated to {settings.Hotkeys.OverlayToggle}");
                    else
                        ShowInfoDialog("Hotkey Registration Failed",
                            $"Could not register \"{settings.Hotkeys.OverlayToggle}\".\n\n" +
                            "The key combination may conflict with another application, " +
                            "or the required system permissions are not granted.\n\n" +
                            "Please try a different key combination in Settings.",
                            primaryText: "OK");
                });
            }

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
        catch (Exception ex)
        {
            Log.Error(ex, "ApplyRuntimeSettings failed");
        }
    }

    private void ShowOverlay(IPlatformServices platform, ISettingsService settingsService)
    {
        try
        {
            ShowOverlayCore(platform, settingsService);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShowOverlay failed");
        }
    }

    private void ShowOverlayCore(IPlatformServices platform, ISettingsService settingsService)
    {
        Log.Debug("ShowOverlayCore invoked");

        if (_activeOverlay is { IsVisible: true })
        {
            Log.Information("Overlay already visible, activating existing window");
            _activeOverlay.Activate();
            return;
        }

        var modelManager = _serviceProvider!.GetRequiredService<IModelManager>();
        if (!IsRequiredModelReady(modelManager))
        {
            Log.Warning("Required model not ready, showing setup wizard instead of overlay");
            _ = ShowSetupWizardAsync(settingsService, modelManager, startStep: 2);
            return;
        }

        var target = platform.WindowTracker.GetForegroundWindowInfo();
        if (target is null)
        {
            Log.Warning("No foreground window detected, skipping overlay");
            return;
        }

        Log.Debug("Foreground window: process={Process}, handle=0x{Handle:X}", target.ProcessName, target.Handle);

        if (OperatingSystem.IsWindows())
        {
            NativeMethods.GetWindowThreadProcessId(target.Handle, out var targetPid);
            if (targetPid == (uint)Environment.ProcessId) return;
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        }
        else
        {
            var selfName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            if (string.Equals(target.ProcessName, selfName, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("Foreground window is self, skipping overlay");
                return;
            }
        }

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
            Log.Debug("Overlay window closed");
            vm.PersistIfChanged();
            _activeOverlay = null;
        };
        _activeOverlay.Show();
        _activeOverlay.Activate();
        Log.Information("Overlay window shown for target process={Process}", target.ProcessName);

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

        if (OperatingSystem.IsWindows())
        {
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
        }
        else if (OperatingSystem.IsMacOS())
        {
            var evt = MacNativeMethods.CGEventCreate(IntPtr.Zero);
            if (evt != IntPtr.Zero)
            {
                try
                {
                    var loc = MacNativeMethods.CGEventGetLocation(evt);
                    var x = (int)loc.X - overlayWidth / 2;
                    var y = (int)loc.Y - estimatedHeight - 12;
                    if (y < 0) y = (int)loc.Y + 20;
                    overlay.Position = new PixelPoint(x, y);
                    return;
                }
                finally
                {
                    MacNativeMethods.CFRelease(evt);
                }
            }
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

    private static void ShowNotification(string message, TimeSpan? duration = null)
    {
        var toast = new NotificationToast(message, duration ?? TimeSpan.FromSeconds(3));
        toast.Show();
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

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void SetMacActivationPolicyAccessory()
    {
        var nsApp = MacNativeMethods.objc_getClass("NSApplication");
        var shared = MacNativeMethods.objc_msgSend(
            nsApp, MacNativeMethods.sel_registerName("sharedApplication"));
        MacNativeMethods.objc_msgSend(
            shared, MacNativeMethods.sel_registerName("setActivationPolicy:"), (IntPtr)1);
    }
}
