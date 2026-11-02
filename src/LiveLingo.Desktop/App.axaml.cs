using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveLingo.Core;
using LiveLingo.Core.Cleanup;
using LiveLingo.Core.Cloud;
using LiveLingo.Core.Dictation;
using LiveLingo.Core.Drafting;
using LiveLingo.Core.Inference;
using LiveLingo.Core.Models;
using LiveLingo.Core.Transcription;
using LiveLingo.Core.Translation;
using LiveLingo.Desktop.Composer;
using LiveLingo.Desktop.Settings;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.Platform.macOS;
using LiveLingo.Desktop.Platform.Windows;
using LiveLingo.Desktop.Services.Cloud;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Services.Preferences;
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
    private UpdateChecks? _updates;
    private NativeMenuItem? _updateItem;
    private string? _registeredGesture;
    private ComposerWindow? _composer;
    private readonly RecentDrafts _recentDrafts = new();
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

        services.AddSingleton<IPreferencesStore>(sp =>
            new JsonPreferencesStore(Path.Combine(GetUserDataDirectory(), "preferences.json"), sp.GetService<ILogger<JsonPreferencesStore>>()));
        services.AddSingleton<ComposerPreferences>();

        // Registered after AddLiveLingoCore so it replaces the local-only default: roles go to the cloud only when the user enabled them.
        services.AddSingleton<ICloudRoutes, SettingsCloudRoutes>();

        _serviceProvider = services.BuildServiceProvider();
        _serviceProvider.GetRequiredService<ServerPidRegistry>().ReapOrphans();

        if (!string.IsNullOrEmpty(settings.Update.UpdateUrl))
        {
            _updates = new UpdateChecks(
                new VelopackUpdateService(
                    settings.Update.UpdateUrl,
                    _serviceProvider.GetRequiredService<ILogger<VelopackUpdateService>>()),
                TimeSpan.FromHours(Math.Max(1, settings.Update.CheckIntervalHours)),
                _serviceProvider.GetRequiredService<ILogger<UpdateChecks>>());
            _updates.Ready += _ => Dispatcher.UIThread.Post(ShowUpdateReady);
            _updates.Start();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) =>
            {
                _updates?.ApplyOnExit();
                _updates?.Dispose();
                _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Log.CloseAndFlush();
            };

            var platform = _serviceProvider.GetService<IPlatformServices>();
            ApplyTheme(settings);
            SetupTrayIcon(desktop, platform);
            if (platform is not null)
            {
                _composer = new ComposerWindow(
                    _serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ComposerWindow>(), platform.WindowMover);
                _composer.Park();
                RegisterHotkey(platform, settings.Hotkeys.Summon);
                platform.Hotkey.HotkeyTriggered += _ => Dispatcher.UIThread.Post(async () => await SummonAsync(platform));
                settingsService.SettingsChanged += () => Dispatcher.UIThread.Post(() =>
                {
                    ApplySettings(platform, settingsService.Current);
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(SettingsModel settings) => RequestedThemeVariant = settings.Appearance.Theme switch
    {
        "Light" => Avalonia.Styling.ThemeVariant.Light,
        "Dark" => Avalonia.Styling.ThemeVariant.Dark,
        _ => Avalonia.Styling.ThemeVariant.Default
    };

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
        openItem.Click += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            if (platform is not null)
                await SummonAsync(platform);
        });

        var settingsItem = new NativeMenuItem(loc.T("tray.settings"));
        settingsItem.Click += (_, _) => Dispatcher.UIThread.Post(() => ShowSettings());

        _updateItem = new NativeMenuItem(loc.T("tray.checkUpdates"));
        _updateItem.Click += (_, _) => Dispatcher.UIThread.Post(async () => await UpdateFromMenuAsync());

        var quitItem = new NativeMenuItem(loc.T("tray.quit"));
        quitItem.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = loc.T("app.name"),
            Menu = menu
        };
        // Avalonia 12 only shows a tray icon that belongs to the application.
        TrayIcon.SetIcons(this, [_trayIcon]);
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

    private async Task SummonAsync(IPlatformServices platform)
    {
        var window = _composer!;
        if (window.IsPresenting)
        {
            window.RequestHide();
            return;
        }

        var target = platform.WindowTracker.GetForegroundWindowInfo();
        Log.Information("Summon requested: target={Process}", target?.ProcessName ?? "none");

        var services = _serviceProvider!;
        var context = SynchronizationContext.Current;
        var settings = services.GetRequiredService<ISettingsService>().Current;
        var glossary = Glossary.Parse(settings.Composer.Glossary);
        var session = new DraftSession(hasTarget: target is not null, notifyOn: context);
        var options = new ComposerOptions(
            settings.Cloud.Translate, settings.Cloud.Clean,
            Enum.Parse<CommitKey>(settings.Composer.CommitKey), settings.Appearance.Animations, settings.Appearance.Theme,
            settings.Composer.CleanupEnabled);
        var preferences = services.GetRequiredService<ComposerPreferences>();
        var start = preferences.Resolve(target, options.Outputs) with { LastExport = preferences.LastExportFormat };
        var scheduler = new TranslationScheduler(
            session, services.GetRequiredService<ITranslator>(), start.Target, TailIdleDelay,
            glossary: glossary);
        // With cleanup off no scheduler listens to the draft, so no sentence is ever sent to a cleaner.
        var cleanup = settings.Composer.CleanupEnabled
            ? new CleanupScheduler(
                session, services.GetRequiredService<ITextCleaner>(), glossary.Terms, CleanupHoldback, contextUnits: 2)
            : null;
        platform.Microphone.PreferredDevice = settings.Composer.Microphone;
        var voice = new MicrophoneVoiceInput(services.GetRequiredService<IDictationSessionFactory>(), platform.Microphone);
        var vm = new ComposerViewModel(
            session,
            scheduler,
            ComposerModelPlan.Create(
                services.GetRequiredService<CoreOptions>().TranslatorTier,
                cleanInCloud: settings.Cloud.Clean,
                translateInCloud: settings.Cloud.Translate),
            services.GetRequiredService<IModelManager>(),
            services.GetRequiredService<IModelDownloadCoordinator>(),
            platform,
            voice,
            services.GetRequiredService<IAudioFileTranscriber>(),
            glossary.Terms,
            options,
            target,
            context);
        vm.SettingsRequested += section => ShowSettings(section);
        vm.Begin(start);
        vm.ChoiceMade += choice => preferences.Remember(target, choice.Output, choice.Target);
        vm.DefaultsRestored += () => preferences.Forget(start.Remember!.AppId);
        vm.Exported += preferences.RememberExportFormat;

        var written = false;
        vm.Completed += () => written = true;

        var presented = window.PresentAsync(vm, new ComposerTexts(services.GetRequiredService<ILocalizationService>()), _recentDrafts, target);
        if (settings.Composer.AutoDictate)
            await vm.StartListeningAsync();
        await presented;

        _recentDrafts.Close(vm.HasTimeline ? "" : vm.SourceText, written);
        await vm.DisposeAsync();
        if (cleanup is not null)
            await cleanup.DisposeAsync();
        await scheduler.DisposeAsync();
    }

    private void ShowSettings(string section = "general")
    {
        if (_settingsWindow is { IsVisible: true } open)
        {
            open.ShowSection(section);
            open.Activate();
            return;
        }

        var services = _serviceProvider!;
        var vm = new SettingsViewModel(
            services.GetRequiredService<ISettingsService>(), services.GetRequiredService<ISecretStore>(),
            services.GetRequiredService<IModelManager>(), _recentDrafts, services.GetRequiredService<IPlatformServices>().Microphone,
            services.GetRequiredService<ComposerPreferences>());
        _settingsWindow = new SettingsWindow(
            vm, new SettingsTexts(services.GetRequiredService<ILocalizationService>()), section,
            services.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsWindow>());
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
        ApplyTheme(settings);
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

    /// <summary>The tray item: restarts into a downloaded update, or looks for one.</summary>
    private async Task UpdateFromMenuAsync()
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        try
        {
            if (_updates is null)
            {
                ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.notConfigured"),
                    primaryText: loc.T("dialog.ok"));
                return;
            }

            if (_updates.ReadyVersion is not null)
            {
                _updates.ApplyAndRestart();
                return;
            }

            if (!await _updates.CheckNowAsync())
            {
                ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.latest"),
                    primaryText: loc.T("dialog.ok"));
                return;
            }

            ShowUpdateReadyDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual update check failed from tray menu.");
            ShowInfoDialog(loc.T("dialog.update.title"), loc.T("dialog.update.failed", ex.Message),
                primaryText: loc.T("dialog.ok"));
        }
    }

    /// <summary>A background download finished: the tray item now restarts into the new version.</summary>
    private void ShowUpdateReady()
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        _updateItem!.Header = loc.T("tray.restartToUpdate", _updates!.ReadyVersion!);
    }

    private void ShowUpdateReadyDialog()
    {
        var loc = _serviceProvider!.GetRequiredService<ILocalizationService>();
        ShowInfoDialog(
            loc.T("dialog.update.ready.title"),
            loc.T("dialog.update.ready.body", _updates!.ReadyVersion!),
            primaryText: loc.T("dialog.update.ready.restart"),
            primaryAction: () =>
            {
                _updates.ApplyAndRestart();
                return Task.CompletedTask;
            },
            secondaryText: loc.T("dialog.update.ready.later"));
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
