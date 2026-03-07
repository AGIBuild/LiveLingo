using LiveLingo.Desktop.ViewModels;
using LiveLingo.Core.Models;
using NSubstitute;

namespace LiveLingo.Desktop.Tests.ViewModels;

public class ModelItemViewModelTests
{
    private static readonly ModelDescriptor TestDescriptor = new(
        "test-model", "Test Model",
        "https://example.com/model.bin",
        104_857_600, ModelType.Translation);

    [Fact]
    public void Properties_ReflectDescriptor()
    {
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        Assert.Equal("test-model", vm.Id);
        Assert.Equal("Test Model", vm.DisplayName);
        Assert.Equal("Translation", vm.TypeLabel);
        Assert.Equal("100 MB", vm.SizeText);
        Assert.False(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task DownloadAsync_Success_SetsInstalled()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.True(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task DownloadAsync_Error_SetsErrorMessage()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("network fail"));

        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsInstalled);
        Assert.False(vm.IsDownloading);
        Assert.Contains("network fail", vm.ErrorMessage);
    }

    [Fact]
    public async Task DownloadAsync_Cancelled_SetsMessage()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException());

        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("Cancelled", vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task DownloadAsync_SkipsWhenAlreadyInstalled()
    {
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: true);

        await vm.DownloadCommand.ExecuteAsync(null);

        await mm.DidNotReceive().EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_SkipsWhenAlreadyDownloading()
    {
        var mm = Substitute.For<IModelManager>();
        var tcs = new TaskCompletionSource();
        mm.EnsureModelAsync(Arg.Any<ModelDescriptor>(),
                Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        var first = vm.DownloadCommand.ExecuteAsync(null);
        await Task.Delay(50);
        await vm.DownloadCommand.ExecuteAsync(null);
        tcs.SetResult();
        await first;

        await mm.Received(1).EnsureModelAsync(
            Arg.Any<ModelDescriptor>(),
            Arg.Any<IProgress<ModelDownloadProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelDownload_CancelsRunningDownload()
    {
        var mm = Substitute.For<IModelManager>();
        mm.EnsureModelAsync(
                Arg.Any<ModelDescriptor>(),
                Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var ct = call.ArgAt<CancellationToken>(2);
                await Task.Delay(3000, ct);
            });

        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        var run = vm.DownloadCommand.ExecuteAsync(null);
        await Task.Delay(80);
        vm.CancelDownloadCommand.Execute(null);
        await run;

        Assert.Equal("Cancelled", vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public void CancelDownload_NoActiveDownload_DoesNotThrow()
    {
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        vm.CancelDownloadCommand.Execute(null);
    }

    [Fact]
    public async Task DeleteAsync_Success_ClearsInstalled()
    {
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: true);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.False(vm.IsInstalled);
        await mm.Received(1).DeleteModelAsync("test-model", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_SkipsWhenNotInstalled()
    {
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: false);

        await vm.DeleteCommand.ExecuteAsync(null);

        await mm.DidNotReceive().DeleteModelAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_Error_SetsErrorMessage()
    {
        var mm = Substitute.For<IModelManager>();
        mm.DeleteModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("delete failed"));
        var vm = new ModelItemViewModel(TestDescriptor, mm, isInstalled: true);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("delete failed", vm.ErrorMessage);
        Assert.True(vm.IsInstalled);
    }

    [Fact]
    public void CreateAll_MatchesRegistryCount()
    {
        var mm = Substitute.For<IModelManager>();
        mm.ListInstalled().Returns([]);

        var models = ModelItemViewModel.CreateAll(mm);

        Assert.Equal(ModelRegistry.AllModels.Count, models.Count);
    }

    [Fact]
    public void CreateAll_MarksInstalledModels()
    {
        var mm = Substitute.For<IModelManager>();
        mm.ListInstalled().Returns([
            new InstalledModel("lid.176.ftz", "FastText", "/path", 917_391,
                ModelType.LanguageDetection, DateTime.UtcNow)
        ]);

        var models = ModelItemViewModel.CreateAll(mm);
        var fastText = models.First(m => m.Id == "lid.176.ftz");
        var marian = models.First(m => m.Id == "opus-mt-zh-en");

        Assert.True(fastText.IsInstalled);
        Assert.False(marian.IsInstalled);
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
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(descriptor, mm, false);

        Assert.Equal(expected, vm.SizeText);
    }

    [Theory]
    [InlineData(ModelType.Translation, "Translation")]
    [InlineData(ModelType.PostProcessing, "PostProcessing")]
    [InlineData(ModelType.LanguageDetection, "LanguageDetection")]
    public void TypeLabel_MatchesModelType(ModelType type, string expected)
    {
        var descriptor = new ModelDescriptor("t", "T", "https://x", 1024, type);
        var mm = Substitute.For<IModelManager>();
        var vm = new ModelItemViewModel(descriptor, mm, false);

        Assert.Equal(expected, vm.TypeLabel);
    }
}
