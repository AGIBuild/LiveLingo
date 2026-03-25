using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Services.Localization;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.Views;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using LiveLingo.Core.Translation;
using NSubstitute;
using System.Reflection;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.SmokeTests;

public class AppStartupSmokeTests
{
    private static IModelManager CreateModelManager(bool requiredModelsInstalled = false)
    {
        var mm = Substitute.For<IModelManager>();
        if (requiredModelsInstalled)
        {
            var installed = ModelRegistry.GetRequiredModelsForLanguagePair("zh", "en")
                .Select(m => new InstalledModel(
                    m.Id, m.DisplayName, "/path", m.SizeBytes, m.Type, DateTime.UtcNow))
                .ToArray();
            mm.ListInstalled().Returns(installed);
            mm.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(true);
        }
        else
        {
            mm.ListInstalled().Returns([]);
        }
        return mm;
    }

    [AvaloniaFact]
    public void SetupWizard_CanBeShownAsStandaloneWindow()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());

        var vm = new SetupWizardViewModel(settingsService, CreateModelManager());
        var wizard = new SetupWizardWindow(vm);

        wizard.Show();
        Assert.True(wizard.IsVisible);

        wizard.Close();
    }

    [AvaloniaFact]
    public void SetupWizard_StepVisibility_ThreeSteps()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());

        var vm = new SetupWizardViewModel(settingsService, CreateModelManager());

        Assert.True(vm.IsStep0);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.Equal(1, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.True(vm.IsStep1);
        Assert.False(vm.IsLastStep);
        Assert.Equal(2, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.True(vm.IsStep2);
        Assert.True(vm.IsLastStep);
        Assert.Equal(3, vm.DisplayStep);
    }

    [AvaloniaFact]
    public void SetupWizard_ModelOnlyMode_StartsAtStep2()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());

        var vm = new SetupWizardViewModel(settingsService, CreateModelManager(), startStep: 2);

        Assert.True(vm.IsStep2);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanGoBack);
    }

    [AvaloniaFact]
    public void SetupWizard_ShowsModelInstalled_WhenRequiredModelsPresent()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());

        var vm = new SetupWizardViewModel(settingsService, CreateModelManager(requiredModelsInstalled: true));

        Assert.True(vm.IsModelInstalled);
    }

    [AvaloniaFact]
    public void SettingsWindow_CanBeCreatedAndShown()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        var vm = new SettingsViewModel(settingsService);

        var window = new SettingsWindow(vm);
        window.Show();

        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void App_ShowSettings_AppliesRequestedTab_AndReusesExistingWindow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", provider.GetRequiredService<IMessenger>());

        app.ShowSettings(3);

        var firstWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var firstVm = Assert.IsType<SettingsViewModel>(firstWindow.DataContext);
        Assert.True(firstWindow.IsVisible);
        Assert.Equal(3, firstVm.SelectedTabIndex);

        app.ShowSettings(2);

        var reusedWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var reusedVm = Assert.IsType<SettingsViewModel>(reusedWindow.DataContext);
        Assert.Same(firstWindow, reusedWindow);
        Assert.Same(firstVm, reusedVm);
        Assert.Equal(2, reusedVm.SelectedTabIndex);

        reusedWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public void SetupWizard_OpenAdvancedRequest_OpensSettingsWindowOnAdvancedTab()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        var settingsService = provider.GetRequiredService<ISettingsService>();
        var wizardVm = new SetupWizardViewModel(
            settingsService,
            messenger: messenger,
            coreOptions: provider.GetRequiredService<CoreOptions>());

        wizardVm.OpenAdvancedForHuggingFaceCommand.Execute(null);

        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);
        Assert.True(settingsWindow.IsVisible);
        Assert.Equal(3, settingsVm.SelectedTabIndex);

        settingsWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SetupWizard_OpenAdvancedAndSaveToken_RestoresWizardStateThroughAppFlow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        var settingsService = provider.GetRequiredService<ISettingsService>();
        var coreOptions = provider.GetRequiredService<CoreOptions>();
        var wizardVm = new SetupWizardViewModel(
            settingsService,
            messenger: messenger,
            coreOptions: coreOptions);

        Assert.False(wizardVm.HasHuggingFaceTokenConfigured);

        wizardVm.OpenAdvancedForHuggingFaceCommand.Execute(null);

        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);
        Assert.Equal(3, settingsVm.SelectedTabIndex);

        settingsVm.WorkingCopy.Advanced.HuggingFaceToken = "hf_end_to_end_token";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("hf_end_to_end_token", coreOptions.HuggingFaceToken);
        Assert.True(wizardVm.HasHuggingFaceTokenConfigured);
        Assert.False(wizardVm.ShowHuggingFaceTokenMissingCallout);

        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_UpdatesActiveOverlayOpacity_ThroughAppRuntimeSync()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        var overlayVm = new OverlayViewModel(
            new TargetWindowInfo(1, 2, "test", "Test", 0, 0, 1000, 700),
            Substitute.For<ITranslationPipeline>(),
            Substitute.For<ITextInjector>(),
            new StubTranslationEngine(),
            settingsService.Current,
            settingsService: settingsService,
            messenger: messenger);
        var overlayWindow = new OverlayWindow(overlayVm);
        overlayWindow.Show();
        overlayWindow.SetBackgroundOpacity(settingsService.Current.UI.OverlayOpacity);
        SetPrivateField(app, "_activeOverlay", overlayWindow);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        var before = GetOverlayFrameAlpha(overlayWindow);
        settingsVm.WorkingCopy.UI.OverlayOpacity = 0.40;

        await settingsVm.SaveCommand.ExecuteAsync(null);

        var after = GetOverlayFrameAlpha(overlayWindow);
        Assert.NotEqual(before, after);
        Assert.Equal((byte)(0.40 * 255), after);

        overlayWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_WhenHotkeyChanges_ReloadsHotkeyThroughAppRuntimeSync()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyId", "overlay");
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.Hotkeys.OverlayToggle = "Ctrl+Shift+Y";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        platform.Hotkey.Received(1).Unregister("overlay");
        platform.Hotkey.Received(1).Register(Arg.Is<HotkeyBinding>(binding =>
            binding.Id == "overlay" &&
            binding.Modifiers == (KeyModifiers.Ctrl | KeyModifiers.Shift) &&
            binding.Key == "Y"));
        Assert.Equal("Ctrl+Shift+Y", GetPrivateField<string>(app, "_currentHotkeyGesture"));

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        settingsWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_WhenHotkeyUnchanged_DoesNotReloadHotkey()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyId", "overlay");
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.UI.OverlayOpacity = 0.7;
        await settingsVm.SaveCommand.ExecuteAsync(null);

        platform.Hotkey.DidNotReceive().Unregister(Arg.Any<string>());
        platform.Hotkey.DidNotReceive().Register(Arg.Any<HotkeyBinding>());

        settingsWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_WhenUiLanguageChanges_UpdatesLocalizationCultureThroughAppRuntimeSync()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        var localization = provider.GetRequiredService<ILocalizationService>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        Assert.Equal("en-US", localization.CurrentCulture.Name);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.UI.Language = "zh-CN";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("zh-CN", localization.CurrentCulture.Name);

        settingsWindow.Close();
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_UpdatesActiveOverlayTargetLanguage_ThroughAppBroadcast()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        var overlayVm = CreateActiveOverlay(app, settingsService, messenger);
        Assert.Equal("en", overlayVm.TargetLanguage);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.Translation.DefaultTargetLanguage = "ja";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("ja", overlayVm.TargetLanguage);

        settingsWindow.Close();
        CloseActiveOverlay(app);
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_UpdatesActiveOverlayInjectionMode_ThroughAppBroadcast()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        var overlayVm = CreateActiveOverlay(app, settingsService, messenger);
        Assert.Equal(InjectionMode.PasteAndSend, overlayVm.Mode);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.UI.DefaultInjectionMode = "PasteOnly";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(InjectionMode.PasteOnly, overlayVm.Mode);

        settingsWindow.Close();
        CloseActiveOverlay(app);
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task AppUiRequest_CloseSettings_ClosesMatchingSettingsWindow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        messenger.Send(new AppUiRequestMessage(new AppUiRequest(settingsVm, AppUiRequestKind.CloseSettings)));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.False(settingsWindow.IsVisible);

        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task AppUiRequest_CloseOverlay_ClosesMatchingOverlayWindow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        var overlayVm = CreateActiveOverlay(app, settingsService, messenger);
        var overlayWindow = GetPrivateField<OverlayWindow>(app, "_activeOverlay");

        messenger.Send(new AppUiRequestMessage(new AppUiRequest(overlayVm, AppUiRequestKind.CloseOverlay)));
        await Task.Delay(250, TestContext.Current.CancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.False(overlayWindow.IsVisible);

        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task AppUiRequest_CloseSetupWizard_ClosesMatchingWizardWindow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        var wizardVm = new SetupWizardViewModel(settingsService, CreateModelManager(), messenger: messenger);
        var wizardWindow = new SetupWizardWindow(wizardVm);
        wizardWindow.Show();
        SetPrivateField(app, "_wizardWindow", wizardWindow);

        messenger.Send(new AppUiRequestMessage(new AppUiRequest(wizardVm, AppUiRequestKind.CloseSetupWizard)));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.False(wizardWindow.IsVisible);

        provider.Dispose();
    }

    [AvaloniaFact]
    public void BuildTrayMenu_CreatesExpectedItems_AndDisablesTranslateWithoutPlatform()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var trayIcon = new TrayIcon();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_trayIcon", trayIcon);

        InvokePrivateMethod(app, "BuildTrayMenu", desktop, null, settingsService);

        Assert.NotNull(trayIcon.Menu);
        Assert.Equal(6, trayIcon.Menu!.Items.Count);
        var translateItem = Assert.IsType<NativeMenuItem>(trayIcon.Menu.Items[0]);
        var settingsItem = Assert.IsType<NativeMenuItem>(trayIcon.Menu.Items[1]);
        var checkUpdateItem = Assert.IsType<NativeMenuItem>(trayIcon.Menu.Items[2]);
        var aboutItem = Assert.IsType<NativeMenuItem>(trayIcon.Menu.Items[3]);
        var separator = trayIcon.Menu.Items[4];
        var quitItem = Assert.IsType<NativeMenuItem>(trayIcon.Menu.Items[5]);

        Assert.Equal("Open Translator", translateItem.Header?.ToString());
        Assert.False(translateItem.IsEnabled);
        Assert.Equal("Settings", settingsItem.Header?.ToString());
        Assert.Equal("Check for Updates", checkUpdateItem.Header?.ToString());
        Assert.Equal("About", aboutItem.Header?.ToString());
        Assert.IsType<NativeMenuItemSeparator>(separator);
        Assert.Equal("Quit", quitItem.Header?.ToString());

        trayIcon.Dispose();
        provider.Dispose();
    }

    [AvaloniaFact]
    public void BuildTrayMenu_UsesCurrentLocalizationCulture()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var trayIcon = new TrayIcon();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        var localization = provider.GetRequiredService<ILocalizationService>();
        localization.SetCulture("zh-CN");
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_trayIcon", trayIcon);

        InvokePrivateMethod(app, "BuildTrayMenu", desktop, platform, settingsService);

        Assert.NotNull(trayIcon.Menu);
        var headers = trayIcon.Menu!.Items
            .OfType<NativeMenuItem>()
            .Select(item => item.Header?.ToString())
            .ToArray();

        Assert.Contains("打开翻译", headers);
        Assert.Contains("设置", headers);
        Assert.Contains("检查更新", headers);
        Assert.Contains("关于", headers);
        Assert.Contains("退出", headers);

        trayIcon.Dispose();
        provider.Dispose();
    }

    [AvaloniaFact]
    public void BuildTrayMenu_ReusesExistingMenu_AndRefreshesLocalizedHeaders()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var trayIcon = new TrayIcon();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        var settingsService = provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        var localization = provider.GetRequiredService<ILocalizationService>();
        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_trayIcon", trayIcon);

        InvokePrivateMethod(app, "BuildTrayMenu", desktop, platform, settingsService);

        var firstMenu = Assert.IsType<NativeMenu>(trayIcon.Menu);
        var firstHeaders = firstMenu.Items
            .OfType<NativeMenuItem>()
            .Select(item => item.Header?.ToString())
            .ToArray();
        Assert.Contains("Open Translator", firstHeaders);

        localization.SetCulture("zh-CN");
        InvokePrivateMethod(app, "BuildTrayMenu", desktop, platform, settingsService);

        var refreshedMenu = Assert.IsType<NativeMenu>(trayIcon.Menu);
        var refreshedHeaders = refreshedMenu.Items
            .OfType<NativeMenuItem>()
            .Select(item => item.Header?.ToString())
            .ToArray();

        Assert.Same(firstMenu, refreshedMenu);
        Assert.Contains("打开翻译", refreshedHeaders);
        Assert.DoesNotContain("Open Translator", refreshedHeaders);

        trayIcon.Dispose();
        provider.Dispose();
    }

    [AvaloniaFact]
    public void OverlayWindow_CanBeCreatedAndShown()
    {
        var pipeline = Substitute.For<ITranslationPipeline>();
        var injector = Substitute.For<LiveLingo.Desktop.Platform.ITextInjector>();
        var target = new LiveLingo.Desktop.Platform.TargetWindowInfo(
            IntPtr.Zero, IntPtr.Zero, "Test", "TestWindow", 0, 0, 800, 600);
        var engine = new StubTranslationEngine();
        var vm = new OverlayViewModel(target, pipeline, injector, engine, "en");

        var window = new OverlayWindow(vm);
        window.Show();

        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OverlayWindow_SourceInputTyping_UpdatesViewModelImmediately()
    {
        var pipeline = Substitute.For<ITranslationPipeline>();
        var injector = Substitute.For<LiveLingo.Desktop.Platform.ITextInjector>();
        var target = new LiveLingo.Desktop.Platform.TargetWindowInfo(
            IntPtr.Zero, IntPtr.Zero, "Test", "TestWindow", 0, 0, 800, 600);
        var engine = new StubTranslationEngine();
        var vm = new OverlayViewModel(target, pipeline, injector, engine, "en");

        var window = new OverlayWindow(vm);
        window.Show();

        var sourceInput = window.FindControl<TextBox>("SourceInput");
        Assert.NotNull(sourceInput);

        sourceInput!.Text = "hello";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal("hello", vm.SourceText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task OverlayWindow_TypingText_ShowsTranslatedResult()
    {
        var pipeline = Substitute.For<ITranslationPipeline>();
        pipeline.ProcessAsync(Arg.Any<TranslationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TranslationResult("translated", "zh", "translated", TimeSpan.FromMilliseconds(5), null));
        var injector = Substitute.For<LiveLingo.Desktop.Platform.ITextInjector>();
        var target = new LiveLingo.Desktop.Platform.TargetWindowInfo(
            IntPtr.Zero, IntPtr.Zero, "Test", "TestWindow", 0, 0, 800, 600);
        var engine = new StubTranslationEngine();
        var vm = new OverlayViewModel(target, pipeline, injector, engine, "en");

        var window = new OverlayWindow(vm);
        window.Show();

        var sourceInput = window.FindControl<TextBox>("SourceInput");
        Assert.NotNull(sourceInput);

        sourceInput!.Text = "hello";
        for (var i = 0; i < 20 && !string.Equals(vm.TranslatedText, "translated", StringComparison.Ordinal); i++)
            await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("translated", vm.TranslatedText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task SettingsSaveButton_WhenAsyncRetryRuns_ClosesSettingsWindow()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var llmCoordinator = provider.GetRequiredService<ILlmModelLoadCoordinator>();
        llmCoordinator.RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            });

        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        RegisterAppUiHandler(app, messenger);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);
        var saveButton = settingsWindow.FindControl<Button>("SaveBtn");

        Assert.NotNull(saveButton);

        settingsVm.WorkingCopy.Advanced.InferenceThreads = settingsVm.WorkingCopy.Advanced.InferenceThreads + 1;
        Assert.NotNull(saveButton!.Command);
        saveButton.Command!.Execute(saveButton.CommandParameter);

        for (var i = 0; i < 20 && settingsWindow.IsVisible; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        Assert.False(settingsWindow.IsVisible);

        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task SettingsSave_WhenAsyncRetryRuns_StillBroadcastsOverlayTranslationSettings()
    {
        var app = new LiveLingo.Desktop.App();
        app.Initialize();

        var provider = CreateAppServiceProvider();
        var messenger = provider.GetRequiredService<IMessenger>();
        var settingsService = (TestSettingsService)provider.GetRequiredService<ISettingsService>();
        var platform = provider.GetRequiredService<IPlatformServices>();
        var llmCoordinator = provider.GetRequiredService<ILlmModelLoadCoordinator>();
        llmCoordinator.RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            });

        SetPrivateField(app, "_serviceProvider", provider);
        SetPrivateField(app, "_messenger", messenger);
        SetPrivateField(app, "_currentHotkeyGesture", settingsService.Current.Hotkeys.OverlayToggle);
        RegisterAppUiHandler(app, messenger);
        RegisterRuntimeSettingsSync(app, platform, settingsService);

        var overlayVm = CreateActiveOverlay(app, settingsService, messenger);
        Assert.Equal("en", overlayVm.TargetLanguage);

        app.ShowSettings();
        var settingsWindow = GetPrivateField<SettingsWindow>(app, "_settingsWindow");
        var settingsVm = Assert.IsType<SettingsViewModel>(settingsWindow.DataContext);

        settingsVm.WorkingCopy.Advanced.InferenceThreads = settingsVm.WorkingCopy.Advanced.InferenceThreads + 1;
        settingsVm.WorkingCopy.Translation.DefaultTargetLanguage = "ja";
        await settingsVm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("ja", overlayVm.TargetLanguage);

        settingsWindow.Close();
        CloseActiveOverlay(app);
        provider.Dispose();
    }

    [AvaloniaFact]
    public async Task FirstRunFlow_WizardThenTrayOnly()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());
        settingsService.SettingsFileExists().Returns(false);

        var wizardVm = new SetupWizardViewModel(settingsService, CreateModelManager());
        var wizard = new SetupWizardWindow(wizardVm);
        var wizardDone = new TaskCompletionSource();
        wizard.Closed += (_, _) => wizardDone.SetResult();
        wizard.Show();

        Assert.True(wizard.IsVisible);

        Dispatcher.UIThread.Post(() => wizard.Close(), DispatcherPriority.Background);
        await wizardDone.Task;
    }

    [AvaloniaFact]
    public void SettingsViewModel_ComboBoxOptions_ArePopulated()
    {
        Assert.NotEmpty(SettingsViewModel.InjectionModes);
        Assert.NotEmpty(SettingsViewModel.PostProcessModes);
        Assert.NotEmpty(SettingsViewModel.LogLevels);
        Assert.Contains("PasteAndSend", SettingsViewModel.InjectionModes);
        Assert.Contains("Off", SettingsViewModel.PostProcessModes);
        Assert.Contains("Information", SettingsViewModel.LogLevels);
    }

    [AvaloniaFact]
    public void NotificationToast_CanBeCreatedAndShown()
    {
        var toast = new NotificationToast("Test issue", TimeSpan.FromSeconds(30));
        toast.Show();

        Assert.True(toast.IsVisible);
        toast.Close();
    }

    [AvaloniaFact]
    public void NotificationToast_ConfigureRequested_Fires()
    {
        var toast = new NotificationToast("Model missing", TimeSpan.FromSeconds(30));
        bool fired = false;
        toast.ConfigureRequested += () => fired = true;

        toast.Show();
        var btn = toast.FindControl<Avalonia.Controls.Button>("ConfigureButton");
        btn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.True(fired);
    }

    [Fact]
    public void HealthCheck_DetectsModelMissing()
    {
        var mm = CreateModelManager(requiredModelsInstalled: false);
        var installed = mm.ListInstalled();
        var allReady = ModelRegistry.RequiredModels.All(
            req => installed.Any(m => m.Id == req.Id));

        Assert.False(allReady);
    }

    [Fact]
    public void HealthCheck_PassesWhenModelInstalled()
    {
        var mm = CreateModelManager(requiredModelsInstalled: true);
        var installed = mm.ListInstalled();
        var allReady = ModelRegistry.RequiredModels.All(
            req => installed.Any(m => m.Id == req.Id));

        Assert.True(allReady);
    }

    [Fact]
    public void CollectStartupIssues_ReportsModelMissing_WhenAssetsAreIncomplete()
    {
        var mm = CreateModelManager(requiredModelsInstalled: true);
        mm.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(false);
        var loc = new LocalizationService();

        var issues = App.CollectStartupIssues(mm, new UserSettings(), loc);

        Assert.Single(issues);
        Assert.Contains("model", issues[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildQwenFallbackNotificationMessage_UsesLocalizationWhenAvailable()
    {
        var loc = new LocalizationService();

        var message = App.BuildQwenFallbackNotificationMessage(
            loc,
            new QwenModelFallbackEventArgs
            {
                Primary = ModelRegistry.Qwen35_9B,
                Fallback = ModelRegistry.Qwen25_15B
            });

        Assert.Contains(ModelRegistry.Qwen35_9B.DisplayName, message);
        Assert.Contains(ModelRegistry.Qwen25_15B.DisplayName, message);
        Assert.DoesNotContain("could not load; using", message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateAppServiceProvider()
    {
        var settingsService = CreateMutableSettingsService();

        var modelManager = Substitute.For<IModelManager>();
        modelManager.ListInstalled().Returns([]);

        var platform = Substitute.For<IPlatformServices>();
        platform.Hotkey.Returns(Substitute.For<IHotkeyService>());
        platform.WindowTracker.Returns(Substitute.For<IWindowTracker>());
        platform.TextInjector.Returns(Substitute.For<ITextInjector>());
        platform.Clipboard.Returns(Substitute.For<IClipboardService>());
        platform.AudioCapture.Returns(Substitute.For<IAudioCaptureService>());

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<IModelManager>(modelManager);
        services.AddSingleton<ITranslationEngine, StubTranslationEngine>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ILanguageCatalog, LanguageCatalog>();
        services.AddSingleton(new CoreOptions());
        services.AddSingleton(Substitute.For<ILlmModelLoadCoordinator>());
        services.AddSingleton(platform);
        services.AddSingleton<IMessenger>(_ => new WeakReferenceMessenger());
        return services.BuildServiceProvider();
    }

    private static ISettingsService CreateMutableSettingsService(UserSettings? initial = null)
        => new TestSettingsService(initial);

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void RegisterAppUiHandler(LiveLingo.Desktop.App app, IMessenger messenger)
    {
        messenger.Register<LiveLingo.Desktop.App, AppUiRequestMessage>(
            app,
            static (recipient, message) => InvokePrivateMethod(recipient, "HandleAppUiRequest", message));
    }

    private static void RegisterRuntimeSettingsSync(
        LiveLingo.Desktop.App app,
        IPlatformServices platform,
        ISettingsService settingsService)
    {
        settingsService.SettingsChanged += () =>
        {
            InvokePrivateMethod(app, "ApplyRuntimeSettings", platform, settingsService.Current);
            InvokePrivateMethod(app, "BroadcastSettingsChanged");
        };
    }

    private static byte GetOverlayFrameAlpha(OverlayWindow overlayWindow)
    {
        var brush = Assert.IsType<SolidColorBrush>(overlayWindow.Resources["OvFrameBrush"]);
        return brush.Color.A;
    }

    private static OverlayViewModel CreateActiveOverlay(
        LiveLingo.Desktop.App app,
        ISettingsService settingsService,
        IMessenger messenger)
    {
        var overlayVm = new OverlayViewModel(
            new TargetWindowInfo(1, 2, "test", "Test", 0, 0, 1000, 700),
            Substitute.For<ITranslationPipeline>(),
            Substitute.For<ITextInjector>(),
            new StubTranslationEngine(),
            settingsService.Current,
            settingsService: settingsService,
            messenger: messenger);
        var overlayWindow = new OverlayWindow(overlayVm);
        overlayWindow.Show();
        SetPrivateField(app, "_activeOverlay", overlayWindow);
        return overlayVm;
    }

    private static void CloseActiveOverlay(LiveLingo.Desktop.App app)
    {
        var overlayWindow = GetPrivateField<OverlayWindow>(app, "_activeOverlay");
        overlayWindow.Close();
    }

    private static void InvokePrivateMethod(object instance, string methodName, params object?[]? args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(instance, args);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        private SettingsModel _current;

        public TestSettingsService(UserSettings? initial = null)
        {
            _current = (initial ?? new UserSettings()).DeepClone();
        }

        public SettingsModel Current => _current;
        public event Action? SettingsChanged;

        public SettingsModel CloneCurrent() => _current.DeepClone();

        public void Replace(SettingsModel model)
        {
            _current = model.DeepClone();
            SettingsChanged?.Invoke();
        }

        public bool SettingsFileExists() => true;

        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
