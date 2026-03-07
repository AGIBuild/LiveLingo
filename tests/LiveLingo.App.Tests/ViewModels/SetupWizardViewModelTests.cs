using LiveLingo.App.Services.Configuration;
using LiveLingo.App.ViewModels;
using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.App.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private static (SetupWizardViewModel vm, ISettingsService settings, IModelManager models) Create(int startStep = 0)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new UserSettings());
        var models = Substitute.For<IModelManager>();
        models.ListInstalled().Returns([]);
        var vm = new SetupWizardViewModel(settings, models, startStep);
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

        settings.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
    }

    [Fact]
    public void FinishCommand_RaisesRequestClose()
    {
        var (vm, _, _) = Create();
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.FinishCommand.Execute(null);

        Assert.True(closed);
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

        UserSettings? saved = null;
        settings.When(s => s.Update(Arg.Any<Func<UserSettings, UserSettings>>()))
            .Do(c =>
            {
                var mutator = c.ArgAt<Func<UserSettings, UserSettings>>(0);
                saved = mutator(new UserSettings());
            });

        vm.FinishCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal("Alt+Space", saved!.Hotkeys.OverlayToggle);
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
    public async Task DownloadModelAsync_ShowsErrorOnFailure()
    {
        var (vm, _, models) = Create(startStep: 2);

        models.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Network error"));

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Contains("Network error", vm.DownloadStatus!);
    }

    [Fact]
    public async Task DownloadModelAsync_SkipsWhenAlreadyInstalled()
    {
        var (vm, _, models) = Create();
        var installed = new InstalledModel(ModelRegistry.Qwen25_15B.Id, "Qwen", "/path", 100, ModelType.PostProcessing, DateTime.UtcNow);
        models.ListInstalled().Returns([installed]);

        var vm2 = new SetupWizardViewModel(Substitute.For<ISettingsService>(), models);
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
}
