using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveLingo.Core;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Platform.macOS;
using LiveLingo.Desktop.Platform.Windows;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LiveLingo.Desktop;

public partial class App : Application
{
    private const string SummonHotkeyId = "summon";

    private ServiceProvider? _serviceProvider;
    private TrayIcon? _trayIcon;
    private Timer? _updateCheckTimer;
    private IUpdateService? _updateService;
    private string? _registeredGesture;
    private ComposerWindow? _composer;
    private SettingsWindow? _settingsWindow;

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
        var settings = settingsService.Current;

        ConfigureLogging(settings);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
        services.AddLiveLingoCore(opts => ApplyCoreOptions(settings, opts));
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<ILocalizationService>(sp =>
        {
            var loc = new LocalizationService(sp.GetService<ILogger<LocalizationService>>());
            loc.SetCulture(settings.Appearance.Language);
            return loc;
        });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IPlatformServices, WindowsPlatformServices>();
            services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IPlatformServices, MacPlatformServices>();
            services.AddSingleton<ISecretStore, MacKeychainSecretStore>();
        }
        else
        {
            services.AddSingleton<ISecretStore, InMemorySecretStore>();
        }

        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetRequiredService<ServerPidRegistry>().ReapOrphans();

        if (!string.IsNullOrEmpty(settings.Update.UpdateUrl))
        {
            _updateService = new VelopackUpdateService(
                settings.Update.UpdateUrl,
                _serviceProvider.GetRequiredService<ILogger<VelopackUpdateService>>());
            StartUpdateChecks(_updateService, settings.Update.CheckIntervalHours);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _updateCheckTimer?.Dispose();
                _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Log.CloseAndFlush();
            };

            var platform = _serviceProvider.GetService<IPlatformServices>();
            SetupTrayIcon(desktop, platform);
            if (platform is not null)
            {
                RegisterHotkey(platform, settings.Hotkeys.Summon);
                platform.Hotkey.HotkeyTriggered += _ => Dispatcher.UIThread.Post(() => Summon(platform));
                settingsService.SettingsChanged += () => Dispatcher.UIThread.Post(() =>
                {
                    ApplySettings(platform, settingsService.Current);
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyCoreOptions(SettingsModel settings, CoreOptions options)
    {
        options.TranslatorTier = Enum.TryParse<TranslatorTier>(settings.Composer.TranslatorTier, out var tier)
            ? tier
            : TranslatorTier.Fast;
        if (!string.IsNullOrWhiteSpace(settings.Advanced.ModelStoragePath))
            options.ModelStoragePath = settings.Advanced.ModelStoragePath;
        options.InferenceThreads = settings.Advanced.InferenceThreads;
        options.HuggingFaceMirror = settings.Advanced.HuggingFaceMirror;
    }

    private static void ConfigureLogging(SettingsModel settings)
    {
        var logDirectory = Path.Combine(GetUserDataDirectory(), "logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, "livelingo-.log");

        var minimumLevel = ParseSerilogLevel(settings.Advanced.LogLevel);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Write(minimumLevel, "Serilog initialized. Log file pattern: {LogPath}", logPath);
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, IPlatformServices? platform)
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();

        var openItem = new NativeMenuItem(loc.T("tray.openComposer")) { IsEnabled = platform is not null };
        openItem.Click += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (platform is not null)
                Summon(platform);
        });

        var settingsItem = new NativeMenuItem(loc.T("tray.settings"));
        settingsItem.Click += (_, _) => Dispatcher.UIThread.Post(ShowSettings);

        var checkUpdateItem = new NativeMenuItem(loc.T("tray.checkUpdates"));
        checkUpdateItem.Click += (_, _) => Dispatcher.UIThread.Post(async () => await CheckForUpdatesFromMenuAsync());

        var quitItem = new NativeMenuItem(loc.T("tray.quit"));
        quitItem.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(checkUpdateItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = loc.T("app.name"),
            Menu = menu
        };
    }

    private static WindowIcon LoadTrayIcon()
    {
        var iconPath = OperatingSystem.IsWindows()
            ? "avares://LiveLingo.Desktop/Assets/tray-icon.ico"
            : "avares://LiveLingo.Desktop/Assets/tray-icon-mac.png";
        return new WindowIcon(AssetLoader.Open(new Uri(iconPath)));
    }

    private static readonly TimeSpan TailIdleDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan CleanupHoldback = TimeSpan.FromSeconds(2);

    private void Summon(IPlatformServices platform)
    {
        if (_composer is { IsVisible: true } open)
        {
            open.Close();
            return;
        }

        var target = platform.WindowTracker.GetForegroundWindowInfo();
        Log.Information("Summon requested: target={Process}", target?.ProcessName ?? "none");

        var services = _serviceProvider!;
        var context = SynchronizationContext.Current;
        var glossary = Glossary.Parse(services.GetRequiredService<ISettingsService>().Current.Composer.Glossary);
        var session = new DraftSession(hasTarget: target is not null, notifyOn: context);
        var scheduler = new TranslationScheduler(
            session, services.GetRequiredService<ITranslator>(), TargetLanguage.English, TailIdleDelay,
            glossary: glossary);
        var cleanup = new CleanupScheduler(
            session, services.GetRequiredService<ITextCleaner>(), glossary.Terms, CleanupHoldback, contextUnits: 2);
        var voice = new MicrophoneVoiceInput(services.GetRequiredService<IDictationSessionFactory>(), platform.Microphone);
        var vm = new ComposerViewModel(
            session,
            scheduler,
            ComposerModelPlan.Create(services.GetRequiredService<CoreOptions>().TranslatorTier),
            services.GetRequiredService<IModelManager>(),
            services.GetRequiredService<IModelDownloadCoordinator>(),
            platform,
            voice,
            glossary.Terms,
            target,
            context);

        var loc = services.GetRequiredService<ILocalizationService>();
        var window = new ComposerWindow(vm, new ComposerTexts(loc), target);
        window.Closed += async (_, _) =>
        {
            _composer = null;
            await vm.DisposeAsync();
            await cleanup.DisposeAsync();
            await scheduler.DisposeAsync();
        };
        _composer = window;
        window.Show();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }

        var services = _serviceProvider!;
        var vm = new SettingsViewModel(services.GetRequiredService<ISettingsService>());
        _settingsWindow = new SettingsWindow(vm, services.GetRequiredService<ILocalizationService>());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void RegisterHotkey(IPlatformServices platform, string gesture)
    {
        Log.Information("Registering hotkey: {Gesture}", gesture);
        try
        {
            platform.Hotkey.Register(HotkeyParser.Parse(SummonHotkeyId, gesture));
            _registeredGesture = gesture;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register hotkey: {Gesture}", gesture);
            ShowHotkeyFailure(gesture);
        }
    }

    private void ApplySettings(IPlatformServices platform, SettingsModel settings)
    {
        ApplyCoreOptions(settings, _serviceProvider!.GetRequiredService<CoreOptions>());
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        if (!string.Equals(loc.CurrentCulture.Name, settings.Appearance.Language, StringComparison.OrdinalIgnoreCase))
            loc.SetCulture(settings.Appearance.Language);

        if (string.Equals(_registeredGesture, settings.Hotkeys.Summon, StringComparison.OrdinalIgnoreCase))
            return;

        platform.Hotkey.Unregister(SummonHotkeyId);
        RegisterHotkey(platform, settings.Hotkeys.Summon);
    }

    private void ShowHotkeyFailure(string gesture)
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        if (OperatingSystem.IsMacOS())
        {
            ShowInfoDialog(
                loc.T("app.permission.title"),
                loc.T("app.permission.body"),
                primaryText: loc.T("app.permission.openAccessibility"),
                primaryAction: () => OpenMacPrivacyPane("Privacy_Accessibility"),
                secondaryText: loc.T("app.permission.openInputMonitoring"),
                secondaryAction: () => OpenMacPrivacyPane("Privacy_ListenEvent"));
            return;
        }

        ShowInfoDialog(
            loc.T("app.hotkey.registrationFailed.title"),
            loc.T("app.hotkey.registrationFailed.body", gesture),
            primaryText: loc.T("dialog.ok"));
    }

    private static Task OpenMacPrivacyPane(string pane)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"x-apple.systempreferences:com.apple.preference.security?{pane}",
            UseShellExecute = false
        });
        return Task.CompletedTask;
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

            if (!await _updateService.CheckForUpdateAsync())
            {
                ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.latest"),
                    primaryText: loc.T("dialog.ok"));
                return;
            }

            ShowInfoDialog(
                loc.T("dialog.update.available.title"),
                loc.T("dialog.update.available.body", _updateService.AvailableVersion ?? "unknown"),
                primaryText: loc.T("dialog.update.available.download"),
                primaryAction: () => _updateService.DownloadAndApplyAsync(),
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
        string primaryText,
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

        var text = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(text, Dock.Top);

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
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
        dialog.Content = new DockPanel { Margin = new Thickness(16), Children = { text, buttons } };
        dialog.Show();
    }

    private void StartUpdateChecks(IUpdateService updateService, int intervalHours)
    {
        async Task CheckAsync(string failureMessage)
        {
            try { await updateService.CheckForUpdateAsync(); }
            catch (Exception ex) { Log.Warning(ex, failureMessage); }
        }

        _ = Task.Run(() => CheckAsync("Background update check failed"));

        var interval = TimeSpan.FromHours(Math.Max(1, intervalHours));
        _updateCheckTimer = new Timer(
            _ => _ = Task.Run(() => CheckAsync("Periodic update check failed")),
            null, interval, interval);
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
