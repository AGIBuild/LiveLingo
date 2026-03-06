using LiveLingo.App.Services.Configuration;
using LiveLingo.App.ViewModels;
using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.App.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private static (SetupWizardViewModel vm, ISettingsService settings, IModelManager models) Create()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new UserSettings());
        var models = Substitute.For<IModelManager>();
        var vm = new SetupWizardViewModel(settings, models);
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
        Assert.False(vm.IsDownloading);
        Assert.False(vm.IsCompleted);
    }

    [Fact]
    public void Navigation_Forward()
    {
        var (vm, _, _) = Create();

        vm.GoNextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentStep);
        Assert.True(vm.CanGoBack);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanGoNext);
    }

    [Fact]
    public void Navigation_Backward()
    {
        var (vm, _, _) = Create();

        vm.GoNextCommand.Execute(null);
        vm.GoNextCommand.Execute(null);
        vm.GoBackCommand.Execute(null);

        Assert.Equal(1, vm.CurrentStep);
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
    public void FinishCommand_SavesSettings()
    {
        var (vm, settings, _) = Create();
        vm.SourceLanguage = "ja";
        vm.TargetLanguage = "zh";
        vm.OverlayHotkey = "Alt+Space";

        vm.FinishCommand.Execute(null);

        settings.Received(1).Update(Arg.Any<Func<UserSettings, UserSettings>>());
        Assert.True(vm.IsCompleted);
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
    public async Task DownloadModel_Success()
    {
        var (vm, _, models) = Create();

        models.EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsDownloading);
        Assert.Equal("Model ready", vm.DownloadStatus);
    }

    [Fact]
    public async Task DownloadModel_Cancelled()
    {
        var (vm, _, models) = Create();

        models.EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(x => throw new OperationCanceledException());

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Equal("Download cancelled", vm.DownloadStatus);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task DownloadModel_Error()
    {
        var (vm, _, models) = Create();

        models.EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(x => throw new InvalidOperationException("network error"));

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Contains("network error", vm.DownloadStatus);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public void TotalSteps_Is3()
    {
        var (vm, _, _) = Create();
        Assert.Equal(3, vm.TotalSteps);
    }

    [Fact]
    public void OnStepChanged_NotifiesNavigationProperties()
    {
        var (vm, _, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.GoNextCommand.Execute(null);

        Assert.Contains(nameof(vm.CanGoBack), changed);
        Assert.Contains(nameof(vm.CanGoNext), changed);
        Assert.Contains(nameof(vm.IsLastStep), changed);
    }
}
