using LiveLingo.Desktop.Services.Configuration;
using LiveLingo.Desktop.Services.LanguageCatalog;
using LiveLingo.Desktop.Messaging;
using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Desktop.ViewModels.Settings;
using CommunityToolkit.Mvvm.Messaging;
using LiveLingo.Core;
using LiveLingo.Core.Engines;
using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using NSubstitute;
using UserSettings = LiveLingo.Desktop.Services.Configuration.SettingsModel;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class SetupWizardViewModelTests
{
    private static (SetupWizardViewModel vm, ISettingsService settings, IModelManager models, IModelDownloadCoordinator coordinator) Create(
        int startStep = 0,
        IMessenger? messenger = null,
        ILlmModelLoadCoordinator? llmCoordinator = null,
        IReadOnlyList<InstalledModel>? installedModels = null,
        IClipboardService? clipboard = null,
        CoreOptions? coreOptions = null,
        IPlatformServices? platformServices = null,
        IModelDownloadCoordinator? downloadCoordinator = null)
    {
        var current = new UserSettings();
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(_ => current);
        settings.CloneCurrent().Returns(_ => current.DeepClone());
        settings.When(x => x.Replace(Arg.Any<UserSettings>()))
            .Do(ci => current = ci.Arg<UserSettings>().DeepClone());
        var models = Substitute.For<IModelManager>();
        models.ListInstalled().Returns(installedModels ?? []);
        models.HasAllExpectedLocalAssets(Arg.Any<ModelDescriptor>()).Returns(true);
        // Default coordinator: marks every requested download as Installed so the
        // happy-path tests don't have to set up StartAsync / GetState themselves.
        var coordinator = downloadCoordinator ?? CreateAlwaysInstalledCoordinator();
        var vm = new SetupWizardViewModel(
            settings,
            models,
            startStep,
            messenger,
            clipboard: clipboard,
            coreOptions: coreOptions,
            llmCoordinator: llmCoordinator,
            platformServices: platformServices,
            downloadCoordinator: coordinator);
        return (vm, settings, models, coordinator);
    }

    private static IModelDownloadCoordinator CreateAlwaysInstalledCoordinator()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);
        coordinator.GetState(Arg.Any<string>())
            .Returns(call => new ModelDownloadState(
                call.Arg<string>(), ModelDownloadStatus.Installed, 100, null));
        return coordinator;
    }

    [Fact]
    public void InitialState()
    {
        var (vm, _, _, _) = Create();

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
        var (vm, _, _, _) = Create();
        Assert.Equal(3, vm.TotalSteps);
    }

    [Fact]
    public void Navigation_Forward_AllSteps()
    {
        var (vm, _, _, _) = Create();

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
        var (vm, _, _, _) = Create();

        vm.GoNextCommand.Execute(null);
        vm.GoBackCommand.Execute(null);

        Assert.Equal(0, vm.CurrentStep);
        Assert.True(vm.IsStep0);
    }

    [Fact]
    public void GoBack_NoOp_AtFirstStep()
    {
        var (vm, _, _, _) = Create();
        vm.GoBackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentStep);
    }

    [Fact]
    public void GoNext_NoOp_AtLastStep()
    {
        var (vm, _, _, _) = Create();
        vm.GoNextCommand.Execute(null);
        vm.GoNextCommand.Execute(null);
        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.CurrentStep);
    }

    [Fact]
    public void StartStep_SkipsToModelDownload()
    {
        var (vm, _, _, _) = Create(startStep: 2);

        Assert.Equal(2, vm.CurrentStep);
        Assert.True(vm.IsStep2);
        Assert.True(vm.IsLastStep);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void StartStep_CanGoBackOnlyToStartStep()
    {
        var (vm, _, _, _) = Create(startStep: 1);

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
    public async Task FinishCommand_SavesSettings()
    {
        var (vm, settings, _, _) = Create();
        vm.SourceLanguage = "ja";
        vm.TargetLanguage = "zh";
        vm.OverlayHotkey = "Alt+Space";

        await vm.FinishCommand.ExecuteAsync(null);

        settings.Received(1).Replace(Arg.Any<UserSettings>());
    }

    [Fact]
    public async Task FinishCommand_SendsCloseMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequestKind? receivedKind = null;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            receivedKind = message.Value.Kind);
        var (vm, _, _, _) = Create(messenger: messenger);

        await vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal(AppUiRequestKind.CloseSetupWizard, receivedKind);
    }

    [Fact]
    public void DisplayStep_MatchesCurrentStepPlusOne()
    {
        var (vm, _, _, _) = Create();
        Assert.Equal(1, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(2, vm.DisplayStep);

        vm.GoNextCommand.Execute(null);
        Assert.Equal(3, vm.DisplayStep);
    }

    [Fact]
    public void OnStepChanged_NotifiesAllNavigationProperties()
    {
        var (vm, _, _, _) = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.GoNextCommand.Execute(null);
        var snapshot = changed.ToArray();

        Assert.Contains(nameof(vm.CanGoBack), snapshot);
        Assert.Contains(nameof(vm.CanGoNext), snapshot);
        Assert.Contains(nameof(vm.IsLastStep), snapshot);
        Assert.Contains(nameof(vm.IsStep0), snapshot);
        Assert.Contains(nameof(vm.IsStep1), snapshot);
        Assert.Contains(nameof(vm.IsStep2), snapshot);
        Assert.Contains(nameof(vm.DisplayStep), snapshot);
    }

    [Fact]
    public async Task FinishCommand_SavesCorrectLanguageValues()
    {
        var (vm, settings, _, _) = Create();
        vm.SourceLanguage = "ja";
        vm.TargetLanguage = "zh";
        vm.OverlayHotkey = "Alt+Space";

        await vm.FinishCommand.ExecuteAsync(null);
        var saved = settings.Current;

        Assert.Equal("Alt+Space", saved.Hotkeys.OverlayToggle);
        Assert.Equal("ja", saved.Translation.DefaultSourceLanguage);
        Assert.Equal("zh", saved.Translation.DefaultTargetLanguage);
        Assert.Contains(saved.Translation.LanguagePairs, p => p.Source == "ja" && p.Target == "zh");
    }

    [Fact]
    public async Task FinishCommand_WhenModelsInstalled_RequestsLlmRetry()
    {
        var coordinator = Substitute.For<ILlmModelLoadCoordinator>();
        coordinator.RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var installed = ModelRegistry.CandidateTranslationModels
            .Take(1)
            .Select(m => new InstalledModel(m.Id, m.DisplayName, "/p", m.SizeBytes, m.Type, DateTime.UtcNow))
            .ToArray();
        var (vm, _, _, _) = Create(llmCoordinator: coordinator, installedModels: installed);

        await vm.FinishCommand.ExecuteAsync(null);

        await coordinator.Received(1).RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinishCommand_WhenModelsNotInstalled_DoesNotRequestLlmRetry()
    {
        var coordinator = Substitute.For<ILlmModelLoadCoordinator>();
        var (vm, _, _, _) = Create(llmCoordinator: coordinator);

        await vm.FinishCommand.ExecuteAsync(null);

        await coordinator.DidNotReceive().RequestRetryPrimaryTranslationModelAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadModelAsync_SetsInstalledOnSuccess()
    {
        // Default coordinator from Create() reports Installed for every requested model.
        var (vm, _, _, _) = Create(startStep: 2);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.True(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Contains("complete", vm.DownloadStatus!);
    }

    [Fact]
    public async Task DownloadModelAsync_ShowsPerModelProgressWithOrderLabel()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>())
            .Returns(call =>
            {
                var descriptor = call.Arg<ModelDescriptor>();
                // Drive coordinator-style progress events the same way the production
                // coordinator would publish them, so the wizard's StateChanged handler
                // updates DownloadStatus mid-download.
                coordinator.StateChanged += Raise.Event<Action<ModelDownloadState>>(
                    new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Downloading, 50, null));
                coordinator.StateChanged += Raise.Event<Action<ModelDownloadState>>(
                    new ModelDownloadState(descriptor.Id, ModelDownloadStatus.Downloading, 100, null));
                return Task.CompletedTask;
            });
        coordinator.GetState(Arg.Any<string>())
            .Returns(call => new ModelDownloadState(
                call.Arg<string>(), ModelDownloadStatus.Installed, 100, null));
        var (vm, _, _, _) = Create(startStep: 2, downloadCoordinator: coordinator);

        var statusHistory = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.DownloadStatus) && !string.IsNullOrWhiteSpace(vm.DownloadStatus))
                statusHistory.Add(vm.DownloadStatus!);
        };

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Contains(statusHistory, s => s.Contains("(1/1)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadModelAsync_ShowsErrorOnFailure()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);
        coordinator.GetState(Arg.Any<string>())
            .Returns(call => new ModelDownloadState(
                call.Arg<string>(), ModelDownloadStatus.Failed, 0, "Network error"));
        var (vm, _, _, _) = Create(startStep: 2, downloadCoordinator: coordinator);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.True(vm.HasError);
        Assert.Contains("Download failed. You can download it manually from", vm.DownloadStatus!);
    }

    [Fact]
    public async Task DownloadModelAsync_SkipsWhenAlreadyInstalled()
    {
        var (_, _, models, coordinator) = Create();
        var installed = ModelRegistry.CandidateTranslationModels
            .Take(1)
            .Select(m => new InstalledModel(m.Id, m.DisplayName, "/path", m.SizeBytes, m.Type, DateTime.UtcNow))
            .ToArray();
        models.ListInstalled().Returns(installed);

        var stubSettings = Substitute.For<ISettingsService>();
        stubSettings.Current.Returns(new UserSettings());
        stubSettings.CloneCurrent().Returns(new UserSettings());
        var vm2 = new SetupWizardViewModel(stubSettings, models, downloadCoordinator: coordinator);
        Assert.True(vm2.IsModelInstalled);

        await vm2.DownloadModelCommand.ExecuteAsync(null);
        await coordinator.DidNotReceive().StartAsync(Arg.Any<ModelDescriptor>());
    }

    [Fact]
    public async Task DownloadModelAsync_HandlesCancellation()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);
        coordinator.GetState(Arg.Any<string>())
            .Returns(call => new ModelDownloadState(
                call.Arg<string>(), ModelDownloadStatus.Cancelled, 0, null));
        var (vm, _, _, _) = Create(startStep: 2, downloadCoordinator: coordinator);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsModelInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Equal("Cancelled", vm.DownloadStatus);
    }

    [Fact]
    public void CancelDownload_RoutesToCoordinatorForActiveDescriptor()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        // Start returns a task that never completes so we can cancel mid-flight.
        coordinator.StartAsync(Arg.Any<ModelDescriptor>())
            .Returns(_ => new TaskCompletionSource<bool>().Task);
        var (vm, _, _, _) = Create(startStep: 2, downloadCoordinator: coordinator);

        // Kick off but don't await — leaves the wizard in the "active descriptor" state.
        _ = vm.DownloadModelCommand.ExecuteAsync(null);

        vm.CancelDownloadCommand.Execute(null);

        coordinator.Received().Cancel(Arg.Any<string>());
    }

    [Fact]
    public async Task DownloadModelAsync_HuggingFaceAuth_ShowsAuthRecoveryMessage()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        coordinator.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);
        coordinator.GetState(Arg.Any<string>())
            .Returns(call => new ModelDownloadState(
                call.Arg<string>(),
                ModelDownloadStatus.Failed,
                0,
                ModelDownloadErrorCodes.HuggingFaceAuthorization));
        var (vm, _, _, _) = Create(startStep: 2, downloadCoordinator: coordinator);

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Contains("token", vm.DownloadStatus!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dispose_UnsubscribesFromCoordinator()
    {
        var coordinator = Substitute.For<IModelDownloadCoordinator>();
        var (vm, _, _, _) = Create(downloadCoordinator: coordinator);

        vm.Dispose();

        // After dispose, raised events must not throw or call back into the wizard.
        coordinator.StateChanged += Raise.Event<Action<ModelDownloadState>>(
            new ModelDownloadState("any", ModelDownloadStatus.Downloading, 50, null));
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

    [Fact]
    public void OpenAdvancedForHuggingFaceCommand_SendsOpenSettingsAdvancedRequest()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        AppUiRequest? received = null;
        messenger.Register<object, AppUiRequestMessage>(recipient, (_, message) =>
            received = message.Value);
        var (vm, _, _, _) = Create(messenger: messenger);

        vm.OpenAdvancedForHuggingFaceCommand.Execute(null);

        Assert.NotNull(received);
        Assert.Equal(AppUiRequestKind.OpenSettings, received!.Kind);
        Assert.Equal((int)SettingsTab.Advanced, received.SettingsInitialTabIndex);
    }

    [Fact]
    public void SettingsChangedMessage_RefreshesHuggingFaceTokenUiState()
    {
        var messenger = new WeakReferenceMessenger();
        var coreOptions = new CoreOptions();
        var (vm, _, _, _) = Create(messenger: messenger, coreOptions: coreOptions);

        Assert.False(vm.HasHuggingFaceTokenConfigured);
        Assert.True(vm.ShowHuggingFaceTokenMissingCallout);

        coreOptions.HuggingFaceToken = "hf_test_token";
        messenger.Send(new SettingsChangedMessage());

        Assert.True(vm.HasHuggingFaceTokenConfigured);
        Assert.False(vm.ShowHuggingFaceTokenMissingCallout);
    }

    [Fact]
    public async Task CopyUrlCommand_CopiesSelectedCandidateModelDownloadUrl()
    {
        var clipboard = Substitute.For<IClipboardService>();
        var (vm, _, _, _) = Create(startStep: 2, clipboard: clipboard);

        await vm.CopyUrlCommand.ExecuteAsync(null);

        await clipboard.Received(1).SetTextAsync(vm.SelectedCandidateModel!.DownloadUrl, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OpenRequiredModelOnHuggingFaceCommand_OpensResolvedModelCardUrl()
    {
        var platform = Substitute.For<IPlatformServices>();
        var (vm, _, _, _) = Create(startStep: 2, platformServices: platform);

        vm.OpenRequiredModelOnHuggingFaceCommand.Execute(null);

        platform.Received(1).OpenUrl("https://huggingface.co/bartowski/google_gemma-4-26B-A4B-it-GGUF");
    }

    [Fact]
    public void SourceLanguageChange_RefreshesModelInstalledState()
    {
        var installed = ModelRegistry.CandidateTranslationModels
            .Take(1)
            .Select(m => new InstalledModel(m.Id, m.DisplayName, "/path", m.SizeBytes, m.Type, DateTime.UtcNow))
            .ToArray();
        var (vm, _, models, coordinator) = Create(installedModels: installed);

        Assert.True(vm.IsModelInstalled);

        models.ListInstalled().Returns([]);
        vm.SourceLanguage = "ja";

        Assert.False(vm.IsModelInstalled);
    }

    [Fact]
    public void ShowOpenRequiredModelOnHuggingFace_IsFalseWithoutPlatform()
    {
        var (vm, _, _, _) = Create(startStep: 2);

        Assert.False(vm.ShowOpenRequiredModelOnHuggingFace);
    }

    [Fact]
    public void ShowOpenModelPageOnDownloadFailure_RequiresErrorAndPlatform()
    {
        var platform = Substitute.For<IPlatformServices>();
        var (vm, _, _, _) = Create(startStep: 2, platformServices: platform);

        Assert.False(vm.ShowOpenModelPageOnDownloadFailure);

        vm.HasError = true;

        Assert.True(vm.ShowOpenModelPageOnDownloadFailure);
    }
}
