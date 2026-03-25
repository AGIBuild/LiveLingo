using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public sealed class QwenModelHostTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"LiveLingo.QwenHost.{Guid.NewGuid():N}");

    public QwenModelHostTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task GetOrStartServerAsync_keeps_shared_startup_running_when_first_waiter_cancels()
    {
        var modelManager = Substitute.For<IModelManager>();
        var serverManager = Substitute.For<ILlamaServerProcessManager>();
        var logger = Substitute.For<ILogger<QwenModelHost>>();

        var modelDir = Path.Combine(_tempDir, ModelRegistry.Qwen35_9B.Id);
        Directory.CreateDirectory(modelDir);
        await File.WriteAllTextAsync(
            Path.Combine(modelDir, "Qwen3.5-9B-abliterated-Q4_K_M.gguf"),
            "stub");

        modelManager.GetModelDirectory(ModelRegistry.Qwen35_9B.Id).Returns(modelDir);
        modelManager.EnsureModelAsync(Arg.Any<ModelDescriptor>(), Arg.Any<IProgress<ModelDownloadProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

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

        using var host = new QwenModelHost(
            modelManager,
            serverManager,
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
