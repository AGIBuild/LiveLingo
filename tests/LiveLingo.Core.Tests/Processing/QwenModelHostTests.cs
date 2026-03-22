using LiveLingo.Core.Models;
using LiveLingo.Core.Processing;
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
        var nativeUpdater = Substitute.For<INativeRuntimeUpdater>();
        modelManager.GetModelDirectory(Arg.Any<string>())
            .Returns(ci => Path.Combine(modelStoragePath, ci.ArgAt<string>(0)));
        return new QwenModelHost(modelManager, nativeUpdater, opts, logger);
    }

    [Fact]
    public void InitialState_IsUnloaded()
    {
        using var host = CreateHost();

        Assert.Equal(ModelLoadState.Unloaded, host.State);
        Assert.Same(ModelRegistry.Qwen35_9B, host.ActiveModelDescriptor);
    }

    [Fact]
    public void ModelPath_InitiallyEmpty()
    {
        using var host = CreateHost();

        Assert.Equal(string.Empty, host.ModelPath);
    }

    [Fact]
    public void CreateExecutorModelParams_ThrowsWhenModelPathEmpty()
    {
        using var host = CreateHost();

        var ex = Assert.Throws<InvalidOperationException>(() => host.CreateExecutorModelParams());
        Assert.Contains("GetWeightsAsync", ex.Message, StringComparison.Ordinal);
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

    [Fact]
    public void ResolvePrimaryGgufPath_ReturnsNull_WhenOnlyStaleGgufPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ll_qwen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Qwen2.5-7B-Instruct-Q4_K_M.gguf-old"), new string('x', 4096));
            var path = QwenModelHost.ResolvePrimaryGgufPath(dir, ModelRegistry.Qwen35_9B);
            Assert.Null(path);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void ResolvePrimaryGgufPath_PrefersUrlFileName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ll_qwen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var expected = "Qwen3.5-9B-abliterated-Q4_K_M.gguf";
            var full = Path.Combine(dir, expected);
            File.WriteAllText(full, "x");
            var path = QwenModelHost.ResolvePrimaryGgufPath(dir, ModelRegistry.Qwen35_9B);
            Assert.Equal(full, path);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
