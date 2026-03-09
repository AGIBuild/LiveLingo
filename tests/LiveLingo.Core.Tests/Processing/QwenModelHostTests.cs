using LiveLingo.Core.Processing;
using LiveLingo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public class QwenModelHostTests
{
    private static QwenModelHost CreateHost(string modelStoragePath = "/fake")
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = modelStoragePath });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        var modelManager = Substitute.For<IModelManager>();
        modelManager.GetModelDirectory(ModelRegistry.Qwen25_15B.Id)
            .Returns(Path.Combine(modelStoragePath, ModelRegistry.Qwen25_15B.Id));
        return new QwenModelHost(modelManager, opts, logger);
    }

    [Fact]
    public void InitialState_IsUnloaded()
    {
        using var host = CreateHost();

        Assert.Equal(ModelLoadState.Unloaded, host.State);
    }

    [Fact]
    public void ModelPath_InitiallyEmpty()
    {
        using var host = CreateHost();

        Assert.Equal(string.Empty, host.ModelPath);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var host = CreateHost();
        host.Dispose();
    }

    [Fact]
    public async Task GetWeightsAsync_ThrowsOnCancellation()
    {
        using var host = CreateHost();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.GetWeightsAsync(cts.Token));
    }

    [Fact]
    public void StateChanged_IsRaisable()
    {
        using var host = CreateHost();

        var states = new List<ModelLoadState>();
        host.StateChanged += s => states.Add(s);

        Assert.Empty(states);
    }
}
