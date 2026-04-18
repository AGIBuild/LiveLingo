using LiveLingo.Desktop.Platform;
using LiveLingo.Desktop.ViewModels;
using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels;

/// <summary>
/// Behavioural tests for the thin, coordinator-driven <see cref="ModelItemViewModel"/>.
/// The VM no longer owns any <see cref="System.Threading.CancellationTokenSource"/>;
/// download / cancel / delete commands must delegate to <see cref="IModelDownloadCoordinator"/>
/// and <see cref="IModelManager"/>. State flips happen purely via
/// <see cref="IModelDownloadCoordinator.StateChanged"/>.
/// </summary>
public class ModelItemViewModelTests
{
    private static readonly ModelDescriptor TestDescriptor = new(
        "test-model", "Test Model",
        "https://example.com/model.bin",
        104_857_600, ModelType.Translation);

    private static ModelItemViewModel Build(
        ModelDescriptor? descriptor = null,
        IModelManager? modelManager = null,
        IModelDownloadCoordinator? coordinator = null,
        IPlatformServices? platform = null,
        ModelDownloadStatus initialStatus = ModelDownloadStatus.Idle)
    {
        descriptor ??= TestDescriptor;
        modelManager ??= Substitute.For<IModelManager>();
        if (coordinator is null)
        {
            var sub = Substitute.For<IModelDownloadCoordinator>();
            sub.GetState(descriptor.Id).Returns(new ModelDownloadState(
                descriptor.Id, initialStatus,
                initialStatus == ModelDownloadStatus.Installed ? 100 : 0,
                null));
            coordinator = sub;
        }
        return new ModelItemViewModel(descriptor, modelManager, coordinator, platformServices: platform, uiContext: null);
    }

    [Fact]
    public void Properties_ReflectDescriptor()
    {
        var vm = Build();

        Assert.Equal("test-model", vm.Id);
        Assert.Equal("Test Model", vm.DisplayName);
        Assert.Equal("Translation", vm.TypeLabel);
        Assert.Equal("100 MB", vm.SizeText);
        Assert.False(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
        Assert.True(vm.ShowDownloadButton);
    }

    [Fact]
    public void InitialState_Installed_ReflectsInInstalledFlag()
    {
        var vm = Build(initialStatus: ModelDownloadStatus.Installed);

        Assert.True(vm.IsInstalled);
        Assert.False(vm.ShowDownloadButton);
    }

    [Fact]
    public async Task DownloadCommand_DelegatesToCoordinator()
    {
        var coord = Substitute.For<IModelDownloadCoordinator>();
        coord.GetState(Arg.Any<string>()).Returns(new ModelDownloadState("test-model", ModelDownloadStatus.Idle, 0, null));
        coord.StartAsync(Arg.Any<ModelDescriptor>()).Returns(Task.CompletedTask);

        var vm = Build(coordinator: coord);

        await vm.DownloadCommand.ExecuteAsync(null);

        await coord.Received(1).StartAsync(TestDescriptor);
    }

    [Fact]
    public async Task DownloadCommand_NoOpWhenAlreadyInstalled()
    {
        var coord = Substitute.For<IModelDownloadCoordinator>();
        coord.GetState(Arg.Any<string>()).Returns(new ModelDownloadState("test-model", ModelDownloadStatus.Installed, 100, null));

        var vm = Build(coordinator: coord, initialStatus: ModelDownloadStatus.Installed);
        await vm.DownloadCommand.ExecuteAsync(null);

        await coord.DidNotReceive().StartAsync(Arg.Any<ModelDescriptor>());
    }

    [Fact]
    public void CancelDownloadCommand_ForwardsToCoordinator()
    {
        var coord = Substitute.For<IModelDownloadCoordinator>();
        coord.GetState(Arg.Any<string>()).Returns(new ModelDownloadState("test-model", ModelDownloadStatus.Idle, 0, null));

        var vm = Build(coordinator: coord);
        vm.CancelDownloadCommand.Execute(null);

        coord.Received(1).Cancel("test-model");
    }

    [Fact]
    public void StateChanged_Downloading_UpdatesFlags()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Idle);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState("test-model", ModelDownloadStatus.Downloading, 42, null));

        Assert.True(vm.IsDownloading);
        Assert.False(vm.IsInstalled);
        Assert.False(vm.ShowDownloadButton);
        Assert.Equal(42, vm.DownloadProgress);
    }

    [Fact]
    public void StateChanged_Installed_MarksInstalled()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Idle);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState("test-model", ModelDownloadStatus.Installed, 100, null));

        Assert.True(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void StateChanged_Cancelled_SetsLocalizedMessage()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Downloading);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState("test-model", ModelDownloadStatus.Cancelled, 20, null));

        Assert.Equal("Cancelled", vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public void StateChanged_FailedWithAuthCode_MapsToLocalizedHint()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Downloading);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState(
            "test-model", ModelDownloadStatus.Failed, 0, ModelDownloadErrorCodes.HuggingFaceAuthorization));

        Assert.Contains("Access denied by Hugging Face", vm.ErrorMessage);
    }

    [Fact]
    public void StateChanged_FailedWithMessage_SurfacesRawMessage()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Downloading);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState("test-model", ModelDownloadStatus.Failed, 0, "network fail"));

        Assert.Equal("network fail", vm.ErrorMessage);
    }

    [Fact]
    public void StateChanged_ForOtherModel_Ignored()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Idle);
        var vm = Build(coordinator: coord);

        coord.Raise(new ModelDownloadState("other-model", ModelDownloadStatus.Installed, 100, null));

        Assert.False(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public void Dispose_UnsubscribesFromStateChanged()
    {
        var coord = new StubCoordinator("test-model", ModelDownloadStatus.Idle);
        var vm = Build(coordinator: coord);

        vm.Dispose();
        coord.Raise(new ModelDownloadState("test-model", ModelDownloadStatus.Installed, 100, null));

        Assert.False(vm.IsInstalled);
    }

    [Fact]
    public async Task DeleteAsync_Success_NotifiesCoordinator()
    {
        var mm = Substitute.For<IModelManager>();
        var coord = Substitute.For<IModelDownloadCoordinator>();
        coord.GetState("test-model").Returns(new ModelDownloadState("test-model", ModelDownloadStatus.Installed, 100, null));

        var vm = new ModelItemViewModel(TestDescriptor, mm, coord, uiContext: null);
        await vm.DeleteCommand.ExecuteAsync(null);

        await mm.Received(1).DeleteModelAsync("test-model", Arg.Any<CancellationToken>());
        coord.Received(1).NotifyDeleted("test-model");
    }

    [Fact]
    public async Task DeleteAsync_Error_SetsErrorMessage()
    {
        var mm = Substitute.For<IModelManager>();
        mm.DeleteModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("delete failed"));
        var coord = Substitute.For<IModelDownloadCoordinator>();
        coord.GetState("test-model").Returns(new ModelDownloadState("test-model", ModelDownloadStatus.Installed, 100, null));

        var vm = new ModelItemViewModel(TestDescriptor, mm, coord, uiContext: null);
        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("delete failed", vm.ErrorMessage);
    }

    [Fact]
    public void ShowOpenOnHuggingFace_TrueWhenPlatformAndHfResolveUrl()
    {
        var platform = Substitute.For<IPlatformServices>();
        var vm = Build(descriptor: ModelRegistry.Qwen35_9B, platform: platform);
        Assert.True(vm.ShowOpenOnHuggingFace);
    }

    [Fact]
    public void ShowOpenOnHuggingFace_FalseWithoutPlatform()
    {
        var vm = Build(descriptor: ModelRegistry.Qwen35_9B);
        Assert.False(vm.ShowOpenOnHuggingFace);
    }

    [Fact]
    public void OpenOnHuggingFace_InvokesPlatformWithModelCardUrl()
    {
        var platform = Substitute.For<IPlatformServices>();
        var vm = Build(descriptor: ModelRegistry.Qwen35_9B, platform: platform);
        vm.OpenOnHuggingFaceCommand.Execute(null);
        platform.Received(1).OpenUrl("https://huggingface.co/Abhiray/Qwen3.5-9B-abliterated-GGUF");
    }

    [Fact]
    public void CreateAll_MatchesRegistryCount()
    {
        var mm = Substitute.For<IModelManager>();
        mm.ListInstalled().Returns([]);

        var models = ModelItemViewModel.CreateAll(mm, NullModelDownloadCoordinator.Instance);

        Assert.Equal(ModelRegistry.AllModels.Count, models.Count);
    }

    [Theory]
    [InlineData(1024, "1 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(1_073_741_824, "1.0 GB")]
    [InlineData(917_391, "896 KB")]
    [InlineData(0, "0 KB")]
    public void SizeText_FormatsCorrectly(long bytes, string expected)
    {
        var descriptor = new ModelDescriptor("t", "T", "https://x", bytes, ModelType.Translation);
        var vm = Build(descriptor: descriptor);

        Assert.Equal(expected, vm.SizeText);
    }

    [Theory]
    [InlineData(ModelType.Translation, "Translation")]
    [InlineData(ModelType.PostProcessing, "Post-Processing")]
    [InlineData(ModelType.LanguageDetection, "Language Detection")]
    public void TypeLabel_MatchesModelType(ModelType type, string expected)
    {
        var descriptor = new ModelDescriptor("t", "T", "https://x", 1024, type);
        var vm = Build(descriptor: descriptor);

        Assert.Equal(expected, vm.TypeLabel);
    }

    private sealed class StubCoordinator : IModelDownloadCoordinator
    {
        private ModelDownloadState _state;

        public StubCoordinator(string modelId, ModelDownloadStatus initial)
        {
            _state = new ModelDownloadState(modelId, initial, 0, null);
        }

        public event Action<ModelDownloadState>? StateChanged;

        public ModelDownloadState GetState(string modelId) => _state;

        public Task StartAsync(ModelDescriptor descriptor) => Task.CompletedTask;

        public void Cancel(string modelId) { }

        public void NotifyDeleted(string modelId) { }

        public void Raise(ModelDownloadState state)
        {
            _state = state;
            StateChanged?.Invoke(state);
        }
    }
}
