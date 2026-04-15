using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public sealed class LocalLlamaModelHostTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingo.QwenHost.{Guid.NewGuid():N}");

    public LocalLlamaModelHostTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task GetOrStartServerAsync_keeps_shared_startup_running_when_first_waiter_cancels()
    {
        var modelManager = Substitute.For<IModelManager>();
        var serverManager = Substitute.For<ILlamaServerProcessManager>();
        var selector = Substitute.For<IModelSelector>();
        var logger = Substitute.For<ILogger<LocalLlamaModelHost>>();
        var catalog = new StaticModelCatalog();

        var modelDir = Path.Combine(_tempDir, ModelRegistry.Qwen35_9B.Id);
        Directory.CreateDirectory(modelDir);
        await File.WriteAllTextAsync(
            Path.Combine(modelDir, "Qwen3.5-9B-abliterated-Q4_K_M.gguf"),
            "stub");

        modelManager.GetModelDirectory(ModelRegistry.Qwen35_9B.Id).Returns(modelDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        selector.SelectTranslationProfile(Arg.Any<string>(), Arg.Any<string>())
            .Returns(catalog.FindById(ModelRegistry.Qwen35_9B.Id)!);
        selector.SelectPostProcessingProfile()
            .Returns(catalog.FindById(ModelRegistry.Qwen25_15B.Id)!);

        var startupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStartupToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = ModelLoadState.Unloaded;
        string? endpoint = null;

        serverManager.State.Returns(_ => state);
        serverManager.CurrentEndpointUrl.Returns(_ => endpoint);
        serverManager.StopServerAsync().Returns(Task.CompletedTask);
        serverManager.EnsureServerRunningAsync(Arg.Any<string>(), 4096, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                startupEntered.TrySetResult();
                await allowStartupToFinish.Task;
                endpoint = "http://127.0.0.1:50123";
                state = ModelLoadState.Loaded;
            });

        using var host = new LocalLlamaModelHost(
            modelManager,
            serverManager,
            selector,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        using var firstCallerCts = new CancellationTokenSource();
        var firstWait = host.GetOrStartServerAsync(firstCallerCts.Token);

        await startupEntered.Task;
        firstCallerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstWait);

        var secondWait = host.GetOrStartServerAsync(CancellationToken.None);
        allowStartupToFinish.SetResult();

        var readyEndpoint = await secondWait;

        Assert.Equal("http://127.0.0.1:50123", readyEndpoint);
        await modelManager.Received(1)
            .EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>());
        await serverManager.Received(1)
            .EnsureServerRunningAsync(Arg.Any<string>(), 4096, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrStartServerAsync_uses_post_processing_profile_when_requested()
    {
        var modelManager = Substitute.For<IModelManager>();
        var serverManager = Substitute.For<ILlamaServerProcessManager>();
        var selector = Substitute.For<IModelSelector>();
        var logger = Substitute.For<ILogger<LocalLlamaModelHost>>();
        var catalog = new StaticModelCatalog();

        var postModelDir = Path.Combine(_tempDir, ModelRegistry.Qwen25_15B.Id);
        Directory.CreateDirectory(postModelDir);
        await File.WriteAllTextAsync(
            Path.Combine(postModelDir, "qwen2.5-1.5b-instruct-q4_k_m.gguf"),
            "stub");

        modelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id).Returns(postModelDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        selector.SelectTranslationProfile(Arg.Any<string>(), Arg.Any<string>())
            .Returns(catalog.FindById(ModelRegistry.Qwen35_9B.Id)!);
        selector.SelectPostProcessingProfile()
            .Returns(catalog.FindById(ModelRegistry.Qwen25_15B.Id)!);

        var state = ModelLoadState.Unloaded;
        string? endpoint = null;
        serverManager.State.Returns(_ => state);
        serverManager.CurrentEndpointUrl.Returns(_ => endpoint);
        serverManager.StopServerAsync().Returns(Task.CompletedTask);
        serverManager.EnsureServerRunningAsync(Arg.Any<string>(), 4096, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                endpoint = "http://127.0.0.1:50124";
                state = ModelLoadState.Loaded;
                return Task.CompletedTask;
            });

        using var host = new LocalLlamaModelHost(
            modelManager,
            serverManager,
            selector,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        var readyEndpoint = await host.GetOrStartServerAsync(ModelTaskType.PostProcessing, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:50124", readyEndpoint);
        await modelManager.Received(1)
            .EnsureModelAsync(
                Arg.Is<ModelDescriptor>(m => m.Id == ModelRegistry.Qwen25_15B.Id),
                Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrStartServerAsync_uses_requested_translation_profile_descriptor()
    {
        var modelManager = Substitute.For<IModelManager>();
        var serverManager = Substitute.For<ILlamaServerProcessManager>();
        var selector = Substitute.For<IModelSelector>();
        var logger = Substitute.For<ILogger<LocalLlamaModelHost>>();
        var catalog = new StaticModelCatalog();

        var translationProfile = catalog.FindById(ModelRegistry.Qwen25_7B.Id)!;
        var translationModelDir = Path.Combine(_tempDir, ModelRegistry.Qwen25_7B.Id);
        Directory.CreateDirectory(translationModelDir);
        await File.WriteAllTextAsync(
            Path.Combine(translationModelDir, "qwen2.5-7b-instruct-q4_k_m.gguf"),
            "stub");

        modelManager.GetModelDirectory(ModelRegistry.Qwen25_7B.Id).Returns(translationModelDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        selector.SelectTranslationProfile(Arg.Any<string>(), Arg.Any<string>())
            .Returns(catalog.FindById(ModelRegistry.Qwen35_9B.Id)!);
        selector.SelectPostProcessingProfile()
            .Returns(catalog.FindById(ModelRegistry.Qwen25_15B.Id)!);

        var state = ModelLoadState.Unloaded;
        string? endpoint = null;
        serverManager.State.Returns(_ => state);
        serverManager.CurrentEndpointUrl.Returns(_ => endpoint);
        serverManager.StopServerAsync().Returns(Task.CompletedTask);
        serverManager.EnsureServerRunningAsync(Arg.Any<string>(), 4096, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                endpoint = "http://127.0.0.1:50125";
                state = ModelLoadState.Loaded;
                return Task.CompletedTask;
            });

        using var host = new LocalLlamaModelHost(
            modelManager,
            serverManager,
            selector,
            Options.Create(new CoreOptions { ModelStoragePath = _tempDir }),
            logger);

        var readyEndpoint = await host.GetOrStartServerAsync(translationProfile, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:50125", readyEndpoint);
        await modelManager.Received(1)
            .EnsureModelAsync(
                Arg.Is<ModelDescriptor>(m => m.Id == ModelRegistry.Qwen25_7B.Id),
                Arg.Any<IProgress<ModelDownloadProgress>?>(),
                Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
