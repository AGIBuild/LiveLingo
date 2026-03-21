using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using NSubstitute;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private static (SetupWizardViewModel vm, ISettingsService settings, IModelManager models) Create(
        int startStep = 0,
        IMessenger? messenger = null)
    {
        var current = new UserSettings();
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(_ => current);
        settings.CloneCurrent().Returns(_ => current.DeepClone());
        settings.When(x => x.Replace(Arg.Any<UserSettings>()))
            .Do(ci => current = ci.Arg<UserSettings>().DeepClone());
        var models = Substitute.For<IModelManager>();
        models.ListInstalled().Returns([]);
        var vm = new SetupWizardViewModel(settings, models, startStep, messenger);
        return (vm, settings, models);
    }

    [Fact]
    public void InitialState()
    {
        var (vm, _, _) = Create();

        Assert.Equal(0, vm.CurrentStep);
        Assert.Equal("zh", vm.SourceLanguage);
        Assert.Equal("en", vm.TargetLanguage);
        Assert.Equal("Ctrl+Alt+T", vm.OverlayHotkey);
        Assert.True(vm.IsStep0);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsModelInstalled);
    }

    [Fact]
    public void TotalSteps_Is3()
    {
        var (vm, _, _) = Create();
        Assert.Equal(3, vm.TotalSteps);
    }

    [Fact]
    public void Navigation_Forward_AllSteps()
    {
        var (vm, _, _) = Create();

        vm.GoNextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStep);
        Assert.True(vm.IsStep1);
        Assert.False(vm.IsLastStep);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.IsStep2);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void Navigation_Backward()
    {
        var (vm, _, _) = Create();

        vm.GoNextCommand.Execute(null);
        vm.GoBackCommand.Execute(null);

        Assert.Equal(0, vm.CurrentStep);
        Assert.True(vm.IsStep0);
    }

    [Fact]
    public void GoBack_NoOp_AtFirstStep()
    {
        var (vm, _, _) = Create();
        vm.GoBackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentStep);
    }

    [Fact]
    public void GoNext_NoOp_AtLastStep()
    {
        var (vm, _, _) = Create();
        vm.GoNextCommand.Execute(null);
        vm.GoNextCommand.Execute(null);
        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
    }

    [Fact]
    public void StartStep_SkipsToModelDownload()
    {
        var (vm, _, _) = Create(startStep: 2);

        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.IsStep2);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void StartStep_CanGoBackOnlyToStartStep()
    {
        var (vm, _, _) = Create(startStep: 1);

        Assert.Equal(1, vm.CurrentStep);
        Assert.False(vm.CanGoBack);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.CanGoBack);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStep);
        Assert.False(vm.CanGoBack);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStep);
    }

    [Fact]
    public void FinishCommand_SavesSettings()
    {
        var (vm, settings, _) = Create();
        vm.SourceLanguage = "ja";
        vm.TargetLanguage = "zh";
        vm.OverlayHotkey = "Alt+Space";

        vm.FinishCommand.Execute(null);

        settings.Received(1).Replace(Arg.Any<UserSettings>());
    }

    [Fact]
    public void FinishCommand_SendsCloseMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        var (vm, _, _) = Create(messenger: messenger);

        vm.FinishCommand.Execute(null);

        Assert.Equal(AppUiRequestKind.CloseSetupWizard, receivedKind);
    }

    [Fact]
    public void DisplayStep_MatchesCurrentStepPlusOne()
    {
        var (vm, _, _) = Create();
        Assert.Equal(1, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(3, vm.DisplayStep);
    }

    [Fact]
    public void OnStepChanged_NotifiesAllNavigationProperties()
    {
        var (vm, _, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.GoNextCommand.Execute(null);

        Assert.Contains(nameof(vm.CanGoBack), changed);
        Assert.Contains(nameof(vm.CanGoNext), changed);
        Assert.Contains(nameof(vm.IsLastStep), changed);
        Assert.Contains(nameof(vm.IsStep0), changed);
        Assert.Contains(nameof(vm.IsStep1), changed);
        Assert.Contains(nameof(vm.IsStep2), changed);
        Assert.Contains(nameof(vm.DisplayStep), changed);
    }

    [Fact]
    public void FinishCommand_SavesCorrectLanguageValues()
    {
        var (vm, settings, _) = Create();
        vm.SourceLanguage = "ja";
        vm.TargetLanguage = "zh";
        vm.OverlayHotkey = "Alt+Space";

        vm.FinishCommand.Execute(null);
        var saved = settings.Current;

        Assert.Equal("Alt+Space", saved.Hotkeys.OverlayToggle);
        Assert.Equal("ja", saved.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
        Assert.Contains(saved.Translation.LanguagePairs, p => p.Source == "ja" && p.Target == "zh");
    }

    [Fact]
    public async Task DownloadModelAsync_SetsInstalledOnSuccess()
    {
        var (vm, _, models) = Create(startStep: 2);

        models.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.True(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Contains("complete", vm.DownloadStatus!);
    }

    [Fact]
    public async Task DownloadModelAsync_ShowsPerModelProgressWithOrderLabel()
    {
        var (vm, _, models) = Create(startStep: 2);
        var statusHistory = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.DownloadStatus) && !string.IsNullOrWhiteSpace(vm.DownloadStatus))
                statusHistory.Add(vm.DownloadStatus!);
        };

        models.EnsureModelAsync(
                Arg.Any<ModelDescriptor>(),
                Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var descriptor = call.Arg<ModelDescriptor>();
                var progress = call.Arg<IProgress<ModelDownloadProgress>?>();
                progress?.Report(new ModelDownloadProgress(descriptor.Id, descriptor.SizeBytes / 2, descriptor.SizeBytes));
                progress?.Report(new ModelDownloadProgress(descriptor.Id, descriptor.SizeBytes, descriptor.SizeBytes));
                return Task.CompletedTask;
            });

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Contains(statusHistory, s => s.Contains("(1/1)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadModelAsync_ShowsErrorOnFailure()
    {
        var (vm, _, models) = Create(startStep: 2);

        models.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Network error"));

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Contains("Download failed. You can download it manually from", vm.DownloadStatus!);
    }

    [Fact]
    public async Task DownloadModelAsync_SkipsWhenAlreadyInstalled()
    {
        var (vm, _, models) = Create();
        var installed = ModelRegistry.GetRequiredModelsForLanguagePair("zh", "en")
            .Select(m => new InstalledModel(m.Id, m.DisplayName, "/path", m.SizeBytes, m.Type, DateTime.UtcNow))
            .ToArray();
        models.ListInstalled().Returns(installed);

        var stubSettings = Substitute.For<ISettingsService>();
        stubSettings.Current.Returns(new UserSettings());
        stubSettings.CloneCurrent().Returns(new UserSettings());
        var vm2 = new SetupWizardViewModel(stubSettings, models);
        Assert.True(vm2.IsModelInstalled);

        await vm2.DownloadModelCommand.ExecuteAsync(null);
        await models.DidNotReceive().EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadModelAsync_HandlesCancellation()
    {
        var (vm, _, models) = Create(startStep: 2);

        models.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException());

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Equal("Cancelled", vm.DownloadStatus);
    }

    [Fact]
    public void AvailableLanguages_ComesFromCatalog()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new UserSettings());
        settings.CloneCurrent().Returns(new UserSettings());
        var models = Substitute.For<IModelManager>();
        models.ListInstalled().Returns([]);
        var catalog = Substitute.For<ILanguageCatalog>();
        catalog.All.Returns(
        [
            new LanguageInfo("en", "English"),
            new LanguageInfo("ja", "Japanese")
        ]);

        var vm = new SetupWizardViewModel(settings, models, languageCatalog: catalog);

        Assert.Equal(["en", "ja"], vm.AvailableLanguages.Select(l => l.Code));
    }
}
