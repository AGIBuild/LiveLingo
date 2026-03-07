using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LiveLingo.App.Services.Configuration;
using LiveLingo.App.ViewModels;
using LiveLingo.App.Views;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Translation;
using NSubstitute;

namespace LiveLingo.App.Tests.SmokeTests;

public class AppStartupSmokeTests
{
    private static IModelManager CreateModelManager(bool qwenInstalled = false)
    {
        var mm = Substitute.For<IModelManager>();
        if (qwenInstalled)
        {
            var installed = new InstalledModel(
                ModelRegistry.Qwen25_15B.Id, "Qwen", "/path", 100,
                ModelType.PostProcessing, DateTime.UtcNow);
            mm.ListInstalled().Returns([installed]);
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
    public void SetupWizard_ShowsModelInstalled_WhenQwenPresent()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new UserSettings());

        var vm = new SetupWizardViewModel(settingsService, CreateModelManager(qwenInstalled: true));

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
    public void OverlayWindow_CanBeCreatedAndShown()
    {
        var pipeline = Substitute.For<ITranslationPipeline>();
        var injector = Substitute.For<LiveLingo.App.Platform.ITextInjector>();
        var target = new LiveLingo.App.Platform.TargetWindowInfo(
            IntPtr.Zero, IntPtr.Zero, "Test", "TestWindow", 0, 0, 800, 600);
        var engine = new StubTranslationEngine();
        var vm = new OverlayViewModel(target, pipeline, injector, engine, "en");

        var window = new OverlayWindow(vm);
        window.Show();

        Assert.True(window.IsVisible);
        window.Close();
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
        var mm = CreateModelManager(qwenInstalled: false);
        var installed = mm.ListInstalled();
        var allReady = ModelRegistry.RequiredModels.All(
            req => installed.Any(m => m.Id == req.Id));

        Assert.False(allReady);
    }

    [Fact]
    public void HealthCheck_PassesWhenModelInstalled()
    {
        var mm = CreateModelManager(qwenInstalled: true);
        var installed = mm.ListInstalled();
        var allReady = ModelRegistry.RequiredModels.All(
            req => installed.Any(m => m.Id == req.Id));

        Assert.True(allReady);
    }
}
