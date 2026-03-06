using LiveLingo.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LiveLingo.Core.Tests.Processing;

public class QwenModelHostTests
{
    [Fact]
    public void InitialState_IsUnloaded()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        using var host = new QwenModelHost(opts, logger);

        Assert.Equal(ModelLoadState.Unloaded, host.State);
    }

    [Fact]
    public void ModelPath_InitiallyEmpty()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        using var host = new QwenModelHost(opts, logger);

        Assert.Equal(string.Empty, host.ModelPath);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        var host = new QwenModelHost(opts, logger);
        host.Dispose();
    }

    [Fact]
    public async Task GetWeightsAsync_ThrowsOnCancellation()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        using var host = new QwenModelHost(opts, logger);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.GetWeightsAsync(cts.Token));
    }

    [Fact]
    public void StateChanged_IsRaisable()
    {
        var opts = Options.Create(new CoreOptions { ModelStoragePath = "/fake" });
        var logger = Substitute.For<ILogger<QwenModelHost>>();
        using var host = new QwenModelHost(opts, logger);

        var states = new List<ModelLoadState>();
        host.StateChanged += s => states.Add(s);

        Assert.Empty(states);
    }
}
